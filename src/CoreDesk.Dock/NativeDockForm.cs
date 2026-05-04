using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;
using CoreDesk.Application.ViewModels;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoreDesk_Dock;

public sealed class NativeDockForm : Form
{
    private readonly ShellViewModel _viewModel;
    private readonly IDiagnosticsService _diagnostics;
    private readonly int? _parentProcessId;
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _visibilityTimer = new();
    private readonly System.Windows.Forms.Timer _animationTimer = new();
    private readonly Dictionary<string, Image> _iconCache = [];
    private readonly List<DockHitTarget> _hitTargets = [];
    private readonly List<IntPtr> _homeHiddenWindows = [];
    private Bitmap? _blurredBackdrop;
    private Rectangle _blurredBackdropSurface;
    private DateTime _blurredBackdropCapturedAt = DateTime.MinValue;
    private bool _initialized;
    private bool _initializing;
    private bool _isCapturingBackdrop;
    private bool _isAutoHidden;
    private bool _isAnimating;
    private int _visibleTop;
    private int _hiddenTop;
    private int _animationTargetTop;
    private DateTime? _foregroundOverlapSince;
    private DockHitTarget? _pressedTarget;
    private Point _mouseDownLocation;
    private bool _dragStarted;
    private bool _windowsHiddenByHome;
    private bool _isDragHoveringDock;
    private int _dropTargetIndex = -1;
    private string? _pressedVisualId;
    private DateTime _pressedVisualUntil = DateTime.MinValue;

    public NativeDockForm(ShellViewModel viewModel, IDiagnosticsService diagnostics, int? parentProcessId = null)
    {
        _viewModel = viewModel;
        _diagnostics = diagnostics;
        _parentProcessId = parentProcessId;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = TransparencyKeyColor;
        TransparencyKey = TransparencyKeyColor;
        ShowIcon = false;
        Text = "CoreDesk Dock";
        AllowDrop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        PositionDock();

        _refreshTimer.Interval = 1200;
        _refreshTimer.Tick += (_, _) => RefreshDock();
        _refreshTimer.Start();

        _visibilityTimer.Interval = 250;
        _visibilityTimer.Tick += (_, _) => MonitorVisibility();
        _visibilityTimer.Start();

        _animationTimer.Interval = 15;
        _animationTimer.Tick += (_, _) => StepAnimation();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return parameters;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ForceVisible();
        _diagnostics.Info($"Native dock window shown before initialization. Handle={Handle}; Bounds={Bounds.Left},{Bounds.Top},{Bounds.Width},{Bounds.Height}; Visible={Visible}; TopMost={TopMost}.");
        BeginInvoke(new Action(async () =>
        {
            try
            {
                await EnsureInitializedAsync();
                ConfigureWindow();
                ForceVisible();
                _diagnostics.Info($"Native dock initialized. Handle={Handle}; Bounds={Bounds.Left},{Bounds.Top},{Bounds.Width},{Bounds.Height}; Visible={Visible}; TopMost={TopMost}; Pinned={_viewModel.PinnedDockItems.Count}; Running={_viewModel.RunningDockItems.Count}.");
            }
            catch (Exception exception)
            {
                _diagnostics.Error(exception, "Native dock initialization failed after window was shown.");
            }
        }));
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized || _initializing)
        {
            return;
        }

        try
        {
            _initializing = true;
            await _viewModel.InitializeAsync();
            _initialized = true;
        }
        finally
        {
            _initializing = false;
        }

        RefreshDock();
    }

    private void ConfigureWindow()
    {
        PositionDock();
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW);
        Invalidate();
    }

    public void ForceVisible()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!Visible)
        {
            Show();
        }

        if (_isAutoHidden)
        {
            ShowDockAnimated();
        }

        TopMost = true;
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW);
    }

    private void RefreshDock()
    {
        try
        {
            if (!_initialized)
            {
                return;
            }

            _viewModel.Tick();
            PositionDock();
            if (!_isAutoHidden)
            {
                ForceVisible();
            }

            Invalidate();
        }
        catch (Exception exception)
        {
            _diagnostics.Error(exception, "Native dock refresh failed.");
        }
    }

    private void PositionDock()
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        var metrics = GetMetrics();
        var desiredWidth = Math.Min(metrics.WindowWidth, screen.Width - (metrics.ScreenInset * 2));
        var desiredHeight = metrics.WindowHeight;
        Bounds = new Rectangle(
            screen.Left + ((screen.Width - desiredWidth) / 2),
            screen.Bottom - desiredHeight - metrics.BottomInset,
            desiredWidth,
            desiredHeight);
        _visibleTop = screen.Bottom - desiredHeight - metrics.BottomInset;
        _hiddenTop = screen.Bottom - Math.Max(Scale(7, metrics.DpiScale), 4);
        if (_isAutoHidden && !_isAnimating)
        {
            Top = _hiddenTop;
        }

        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        ForceVisible();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressedTarget = null;
        _dragStarted = false;
        foreach (var target in _hitTargets)
        {
            if (!target.Bounds.Contains(e.Location))
            {
                continue;
            }

            _pressedTarget = target;
            _mouseDownLocation = e.Location;
            _pressedVisualId = target.IsHome ? HomeDockItem.App.Id : target.Item.App.Id;
            _pressedVisualUntil = DateTime.UtcNow.AddMilliseconds(220);
            Invalidate();
            return;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressedTarget is null || _dragStarted || e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_pressedTarget.IsHome)
        {
            return;
        }

        var dragSize = SystemInformation.DragSize;
        var dragRect = new Rectangle(
            _mouseDownLocation.X - (dragSize.Width / 2),
            _mouseDownLocation.Y - (dragSize.Height / 2),
            dragSize.Width,
            dragSize.Height);
        if (dragRect.Contains(e.Location))
        {
            return;
        }

        _dragStarted = true;
        var data = new DataObject();
        data.SetText($"coredesk-app:{_pressedTarget.Item.App.Id}");
        DoDragDrop(data, DragDropEffects.Move);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressedTarget is null)
        {
            return;
        }

        var target = _pressedTarget;
        _pressedTarget = null;
        if (!_dragStarted && target.Bounds.Contains(e.Location))
        {
            if (target.IsHome)
            {
                ToggleHomeWindows();
            }
            else
            {
                _ = OpenDockItemAsync(target.Item);
            }
        }

        _dragStarted = false;
        Invalidate();
    }

    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        drgevent.Effect = TryGetDraggedAppId(drgevent.Data, out _) ? DragDropEffects.Move : DragDropEffects.None;
        _isDragHoveringDock = drgevent.Effect != DragDropEffects.None;
        _dropTargetIndex = _isDragHoveringDock ? GetDockTargetIndex(PointToClient(new Point(drgevent.X, drgevent.Y))) : -1;
        PositionDock();
        Invalidate();
        ShowDockAnimated();
    }

    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        drgevent.Effect = TryGetDraggedAppId(drgevent.Data, out _) ? DragDropEffects.Move : DragDropEffects.None;
        var nextIndex = drgevent.Effect == DragDropEffects.None
            ? -1
            : GetDockTargetIndex(PointToClient(new Point(drgevent.X, drgevent.Y)));
        if (_dropTargetIndex != nextIndex || _isDragHoveringDock != (drgevent.Effect != DragDropEffects.None))
        {
            _isDragHoveringDock = drgevent.Effect != DragDropEffects.None;
            _dropTargetIndex = nextIndex;
            PositionDock();
            Invalidate();
        }
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _isDragHoveringDock = false;
        _dropTargetIndex = -1;
        PositionDock();
        Invalidate();
    }

    protected override async void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (!TryGetDraggedAppId(drgevent.Data, out var appId))
        {
            return;
        }

        var clientPoint = PointToClient(new Point(drgevent.X, drgevent.Y));
        var targetIndex = GetDockTargetIndex(clientPoint);
        try
        {
            await _viewModel.MoveDockItemAsync(appId, targetIndex);
            _isDragHoveringDock = false;
            _dropTargetIndex = -1;
            PositionDock();
            Invalidate();
        }
        catch (Exception exception)
        {
            _isDragHoveringDock = false;
            _dropTargetIndex = -1;
            _diagnostics.Error(exception, $"Dock drop failed for app '{appId}'.");
        }
    }

    private async Task OpenDockItemAsync(DockItemViewModel item)
    {
        try
        {
            Cursor = Cursors.Default;
            UseWaitCursor = false;
            if (_viewModel.OpenDockItemCommand.CanExecute(item))
            {
                await _viewModel.OpenDockItemCommand.ExecuteAsync(item);
            }
        }
        catch (Exception exception)
        {
            _diagnostics.Error(exception, $"Opening dock item '{item.DisplayName}' failed.");
        }
        finally
        {
            Cursor = Cursors.Default;
            UseWaitCursor = false;
            ForceVisible();
        }
    }

    private void MonitorVisibility()
    {
        if (IsDisposed || !_initialized)
        {
            return;
        }

        var cursor = Cursor.Position;
        var screen = Screen.PrimaryScreen!.Bounds;
        var hasWindowUnderDock = IsAnyWindowUnderDock();
        if (_isAutoHidden && (!hasWindowUnderDock || cursor.Y >= screen.Bottom - Scale(4, GetMetrics().DpiScale)))
        {
            ShowDockAnimated();
            _foregroundOverlapSince = null;
            return;
        }

        if (!_isAutoHidden && hasWindowUnderDock)
        {
            _foregroundOverlapSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _foregroundOverlapSince.Value >= TimeSpan.FromSeconds(5))
            {
                HideDockAnimated();
            }

            return;
        }

        _foregroundOverlapSince = null;
        if (!_isAutoHidden)
        {
            TopMost = true;
            NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW);
        }
    }

    private bool IsAnyWindowUnderDock()
    {
        var dockRect = new Rectangle(Left, _visibleTop, Width, Height);
        var found = false;
        NativeMethods.EnumWindows((handle, lParam) =>
        {
            if (IsIgnoredWindow(handle) || !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var windowRect = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (!windowRect.IntersectsWith(dockRect))
            {
                return true;
            }

            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private bool IsForegroundWindowUnderDock()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsIgnoredWindow(foreground))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        var windowRect = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var dockRect = new Rectangle(Left, _visibleTop, Width, Height);
        return windowRect.IntersectsWith(dockRect);
    }

    private bool IsIgnoredWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == Handle)
        {
            return true;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        return processId == (uint)Environment.ProcessId
            || (_parentProcessId is not null && processId == (uint)_parentProcessId.Value);
    }

    private void ToggleHomeWindows()
    {
        try
        {
            if (_windowsHiddenByHome)
            {
                RestoreHomeHiddenWindows();
                return;
            }

            HideWindowsForHome();
        }
        catch (Exception exception)
        {
            _diagnostics.Error(exception, "Home dock action failed.");
        }
    }

    private void HideWindowsForHome()
    {
        _homeHiddenWindows.Clear();
        NativeMethods.EnumWindows((handle, lParam) =>
        {
            if (IsIgnoredWindow(handle) || !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(handle, out var rect))
            {
                return true;
            }

            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            {
                return true;
            }

            _homeHiddenWindows.Add(handle);
            NativeMethods.ShowWindowAsync(handle, SW_MINIMIZE);
            return true;
        }, IntPtr.Zero);

        _windowsHiddenByHome = _homeHiddenWindows.Count > 0;
        ActivateParentWindow();
        ForceVisible();
    }

    private void RestoreHomeHiddenWindows()
    {
        foreach (var handle in _homeHiddenWindows.ToArray())
        {
            if (NativeMethods.IsWindow(handle))
            {
                NativeMethods.ShowWindowAsync(handle, SW_RESTORE);
            }
        }

        _homeHiddenWindows.Clear();
        _windowsHiddenByHome = false;
        ForceVisible();
    }

    private void ActivateParentWindow()
    {
        if (_parentProcessId is null)
        {
            return;
        }

        var parentWindow = IntPtr.Zero;
        NativeMethods.EnumWindows((handle, lParam) =>
        {
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == (uint)_parentProcessId.Value && NativeMethods.IsWindowVisible(handle))
            {
                parentWindow = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (parentWindow != IntPtr.Zero)
        {
            _ = NativeMethods.SetForegroundWindow(parentWindow);
        }
    }

    private void HideDockAnimated()
    {
        if (_isAutoHidden)
        {
            return;
        }

        _isAutoHidden = true;
        StartAnimation(_hiddenTop);
    }

    private void ShowDockAnimated()
    {
        if (!_isAutoHidden && !_isAnimating)
        {
            return;
        }

        _isAutoHidden = false;
        StartAnimation(_visibleTop);
    }

    private void StartAnimation(int targetTop)
    {
        _animationTargetTop = targetTop;
        _isAnimating = true;
        if (!_animationTimer.Enabled)
        {
            _animationTimer.Start();
        }
    }

    private void StepAnimation()
    {
        var delta = _animationTargetTop - Top;
        if (Math.Abs(delta) <= 2)
        {
            Top = _animationTargetTop;
            _isAnimating = false;
            _animationTimer.Stop();
        }
        else
        {
            Top += (int)Math.Round(delta * 0.22);
        }

        var progress = _hiddenTop == _visibleTop
            ? 1.0
            : 1.0 - Math.Clamp((Top - _visibleTop) / (double)(_hiddenTop - _visibleTop), 0.0, 1.0);
        Opacity = Math.Clamp(0.18 + (progress * 0.82), 0.18, 1.0);
        NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_SHOWWINDOW);
    }

    private bool IsPressedVisual(DockHitTarget target)
    {
        return _pressedVisualId is not null
            && DateTime.UtcNow <= _pressedVisualUntil
            && _pressedVisualId.Equals(target.Item.App.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDraggedAppId(IDataObject? data, out string appId)
    {
        appId = string.Empty;
        if (data is null || !data.GetDataPresent(DataFormats.UnicodeText) && !data.GetDataPresent(DataFormats.Text))
        {
            return false;
        }

        var text = (data.GetData(DataFormats.UnicodeText) ?? data.GetData(DataFormats.Text)) as string;
        const string prefix = "coredesk-app:";
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        appId = text[prefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(appId);
    }

    private int GetDockTargetIndex(Point point)
    {
        var metrics = GetMetrics();
        var visualCount = Math.Min(Math.Max(0, metrics.VisualItemCount - 1 - (_isDragHoveringDock ? 1 : 0)), 8);
        var contentWidth = (visualCount * metrics.IconSlot) + (Math.Max(0, visualCount - 1) * metrics.ItemGap);
        var x = ((Width - ((metrics.VisualItemCount * metrics.IconSlot) + (Math.Max(0, metrics.VisualItemCount - 1) * metrics.ItemGap))) / 2)
            + metrics.IconSlot
            + metrics.ItemGap;
        for (var index = 0; index < visualCount; index++)
        {
            if (point.X < x + (metrics.IconSlot / 2))
            {
                return index;
            }

            x += metrics.IconSlot + metrics.ItemGap;
        }

        return visualCount;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(TransparencyKeyColor);
        _hitTargets.Clear();

        var metrics = GetMetrics();
        var dockSurface = new Rectangle(metrics.SideShadow, metrics.TopShadow, Width - (metrics.SideShadow * 2), metrics.DockHeight);
        using var path = RoundedRect(dockSurface, metrics.CornerRadius);
        DrawSoftShadow(graphics, dockSurface);
        DrawBlurredBackdrop(graphics, dockSurface, path, metrics);

        using var glass = new LinearGradientBrush(
            dockSurface,
            Color.FromArgb(118, 72, 35, 52),
            Color.FromArgb(92, 28, 18, 30),
            90f);
        using var sheen = new LinearGradientBrush(
            dockSurface,
            Color.FromArgb(132, 255, 255, 255),
            Color.FromArgb(18, 255, 255, 255),
            90f);
        using var stroke = new Pen(Color.FromArgb(92, 255, 255, 255), Math.Max(1f, metrics.DpiScale));

        graphics.FillPath(glass, path);
        graphics.FillPath(sheen, path);
        graphics.DrawPath(stroke, path);
        using (var highlight = new Pen(Color.FromArgb(112, 255, 255, 255), Math.Max(1f, metrics.DpiScale)))
        {
            graphics.DrawLine(highlight, dockSurface.Left + metrics.CornerRadius, dockSurface.Top + metrics.DpiScale, dockSurface.Right - metrics.CornerRadius, dockSurface.Top + metrics.DpiScale);
        }

        var itemCount = metrics.VisualItemCount;
        var contentWidth = (itemCount * metrics.IconSlot) + (Math.Max(0, itemCount - 1) * metrics.ItemGap);
        var x = (Width - contentWidth) / 2;
        var y = dockSurface.Top + ((dockSurface.Height - metrics.IconSlot) / 2);
        DrawHomeItem(graphics, metrics, ref x, y);

        if (!_initialized)
        {
            DrawFallbackItems(graphics, metrics, ref x, y, interactive: true);
            return;
        }

        var appIndex = 0;
        DrawDropSlotIfNeeded(graphics, metrics, ref x, y, appIndex);
        var drawnCount = DrawItems(graphics, _viewModel.PinnedDockItems, metrics, ref x, y, pinned: true, ref appIndex);

        if (_viewModel.RunningDockItems.Count > 0)
        {
            DrawDropSlotIfNeeded(graphics, metrics, ref x, y, appIndex);
            x += metrics.SeparatorGap;
        }

        drawnCount += DrawItems(graphics, _viewModel.RunningDockItems, metrics, ref x, y, pinned: false, ref appIndex);
        DrawDropSlotIfNeeded(graphics, metrics, ref x, y, appIndex);

        if (_parentProcessId is null && _viewModel.PinnedDockItems.Count == 0)
        {
            DrawFallbackItems(graphics, metrics, ref x, y, interactive: true);
        }
    }

    private int GetDockItemCount()
    {
        if (!_initialized)
        {
            return 9;
        }

        var realItemCount = 1
            + Math.Clamp(_viewModel.PinnedDockItems.Count, 0, 8)
            + Math.Clamp(_viewModel.RunningDockItems.Count, 0, 4);
        if (_isDragHoveringDock)
        {
            realItemCount++;
        }

        if (_parentProcessId is null && _viewModel.PinnedDockItems.Count == 0)
        {
            realItemCount += 8;
        }

        return realItemCount;
    }

    private DockMetrics GetMetrics()
    {
        var scale = Math.Clamp(DeviceDpi / 96f, 1f, 2.5f);
        var iconSlot = Scale(86, scale);
        var iconSize = Scale(72, scale);
        var itemGap = Scale(13, scale);
        var sidePadding = Scale(26, scale);
        var itemCount = Math.Max(1, GetDockItemCount());
        var contentWidth = (itemCount * iconSlot) + (Math.Max(0, itemCount - 1) * itemGap);
        var dockWidth = contentWidth + (sidePadding * 2);
        var sideShadow = Scale(17, scale);

        return new DockMetrics(
            scale,
            itemCount,
            iconSlot,
            iconSize,
            itemGap,
            Scale(18, scale),
            Math.Max(Scale(38, scale), iconSize / 2),
            Scale(8, scale),
            Scale(3, scale),
            Scale(9, scale),
            Scale(18, scale),
            Math.Max(Scale(108, scale), iconSlot + Scale(24, scale)),
            dockWidth + (sideShadow * 2),
            Math.Max(Scale(144, scale), iconSlot + Scale(58, scale)),
            Scale(18, scale),
            Scale(34, scale),
            sideShadow);
    }

    private static int Scale(int value, float scale) => Math.Max(1, (int)Math.Round(value * scale));

    private void DrawHomeItem(Graphics graphics, DockMetrics metrics, ref int x, int y)
    {
        var bounds = new Rectangle(x, y, metrics.IconSlot, metrics.IconSlot);
        var target = new DockHitTarget(bounds, HomeDockItem, _hitTargets.Count, IsHome: true);
        DrawHomeIcon(graphics, ApplyPressedVisual(graphics, Centered(bounds, metrics.IconSize), metrics, IsPressedVisual(target)), metrics);
        _hitTargets.Add(target);
        x += metrics.IconSlot + metrics.ItemGap;
    }

    private void DrawDropSlotIfNeeded(Graphics graphics, DockMetrics metrics, ref int x, int y, int appIndex)
    {
        if (!_isDragHoveringDock || _dropTargetIndex != appIndex)
        {
            return;
        }

        var bounds = new Rectangle(x, y, metrics.IconSlot, metrics.IconSlot);
        var slot = Centered(bounds, metrics.IconSize);
        using var path = RoundedRect(slot, Math.Max(12, slot.Width / 5));
        using var fill = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
        using var stroke = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, metrics.DpiScale))
        {
            DashStyle = DashStyle.Dash
        };
        graphics.FillPath(fill, path);
        graphics.DrawPath(stroke, path);
        x += metrics.IconSlot + metrics.ItemGap;
    }

    private void DrawBlurredBackdrop(Graphics graphics, Rectangle dockSurface, GraphicsPath clipPath, DockMetrics metrics)
    {
        if (_isCapturingBackdrop)
        {
            return;
        }

        var shouldRefresh = _blurredBackdrop is null
            || _blurredBackdropSurface != dockSurface
            || DateTime.UtcNow - _blurredBackdropCapturedAt > TimeSpan.FromMilliseconds(_isAutoHidden ? 800 : 260);
        if (shouldRefresh)
        {
            CaptureBlurredBackdrop(dockSurface, metrics);
        }

        if (_blurredBackdrop is null)
        {
            return;
        }

        var previousClip = graphics.Clip;
        graphics.SetClip(clipPath, CombineMode.Replace);
        using var attributes = new ImageAttributes();
        var alpha = _isAutoHidden ? 0.62f : 0.88f;
        attributes.SetColorMatrix(new ColorMatrix
        {
            Matrix00 = 1f,
            Matrix11 = 1f,
            Matrix22 = 1f,
            Matrix33 = alpha,
            Matrix44 = 1f
        });
        graphics.DrawImage(
            _blurredBackdrop,
            dockSurface,
            0,
            0,
            _blurredBackdrop.Width,
            _blurredBackdrop.Height,
            GraphicsUnit.Pixel,
            attributes);

        using var wash = new LinearGradientBrush(
            dockSurface,
            Color.FromArgb(62, 255, 255, 255),
            Color.FromArgb(72, 20, 10, 18),
            90f);
        graphics.FillPath(wash, clipPath);
        graphics.Clip = previousClip;
    }

    private void CaptureBlurredBackdrop(Rectangle dockSurface, DockMetrics metrics)
    {
        if (!IsHandleCreated || dockSurface.Width <= 0 || dockSurface.Height <= 0)
        {
            return;
        }

        _isCapturingBackdrop = true;
        Bitmap? capture = null;
        try
        {
            var screenRect = RectangleToScreen(dockSurface);
            var oldDisplayAffinity = 0u;
            _ = NativeMethods.GetWindowDisplayAffinity(Handle, out oldDisplayAffinity);
            _ = NativeMethods.SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
            NativeMethods.RedrawWindow(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);

            capture = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppPArgb);
            using (var captureGraphics = Graphics.FromImage(capture))
            {
                captureGraphics.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, screenRect.Size, CopyPixelOperation.SourceCopy);
            }

            _ = NativeMethods.SetWindowDisplayAffinity(Handle, oldDisplayAffinity);

            var scale = Math.Clamp(0.28f / metrics.DpiScale, 0.12f, 0.26f);
            var smallWidth = Math.Max(64, (int)Math.Round(capture.Width * scale));
            var smallHeight = Math.Max(18, (int)Math.Round(capture.Height * scale));
            using var small = new Bitmap(smallWidth, smallHeight, PixelFormat.Format32bppPArgb);
            using (var smallGraphics = Graphics.FromImage(small))
            {
                smallGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                smallGraphics.DrawImage(capture, new Rectangle(0, 0, small.Width, small.Height));
            }

            BlurBitmap(small, Math.Max(4, Scale(10, metrics.DpiScale) / 2), passes: 3);

            var blurred = new Bitmap(capture.Width, capture.Height, PixelFormat.Format32bppPArgb);
            using (var blurredGraphics = Graphics.FromImage(blurred))
            {
                blurredGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                blurredGraphics.DrawImage(small, new Rectangle(0, 0, blurred.Width, blurred.Height));
            }

            _blurredBackdrop?.Dispose();
            _blurredBackdrop = blurred;
            _blurredBackdropSurface = dockSurface;
            _blurredBackdropCapturedAt = DateTime.UtcNow;
        }
        catch (Exception exception)
        {
            _diagnostics.Error(exception, "Capturing dock blur backdrop failed.");
        }
        finally
        {
            capture?.Dispose();
            _ = NativeMethods.SetWindowDisplayAffinity(Handle, WDA_NONE);
            _isCapturingBackdrop = false;
        }
    }

    private static void BlurBitmap(Bitmap bitmap, int radius, int passes)
    {
        if (radius <= 0)
        {
            return;
        }

        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = Math.Abs(data.Stride) * bitmap.Height;
            var source = new byte[bytes];
            var target = new byte[bytes];
            Marshal.Copy(data.Scan0, source, 0, bytes);

            for (var pass = 0; pass < passes; pass++)
            {
                BoxBlurHorizontal(source, target, bitmap.Width, bitmap.Height, data.Stride, radius);
                BoxBlurVertical(target, source, bitmap.Width, bitmap.Height, data.Stride, radius);
            }

            Marshal.Copy(source, 0, data.Scan0, bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void BoxBlurHorizontal(byte[] source, byte[] target, int width, int height, int stride, int radius)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var blue = 0;
                var green = 0;
                var red = 0;
                var alpha = 0;
                var count = 0;
                for (var sampleX = Math.Max(0, x - radius); sampleX <= Math.Min(width - 1, x + radius); sampleX++)
                {
                    var offset = row + (sampleX * 4);
                    blue += source[offset];
                    green += source[offset + 1];
                    red += source[offset + 2];
                    alpha += source[offset + 3];
                    count++;
                }

                var destination = row + (x * 4);
                target[destination] = (byte)(blue / count);
                target[destination + 1] = (byte)(green / count);
                target[destination + 2] = (byte)(red / count);
                target[destination + 3] = (byte)(alpha / count);
            }
        }
    }

    private static void BoxBlurVertical(byte[] source, byte[] target, int width, int height, int stride, int radius)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var blue = 0;
                var green = 0;
                var red = 0;
                var alpha = 0;
                var count = 0;
                for (var sampleY = Math.Max(0, y - radius); sampleY <= Math.Min(height - 1, y + radius); sampleY++)
                {
                    var offset = (sampleY * stride) + (x * 4);
                    blue += source[offset];
                    green += source[offset + 1];
                    red += source[offset + 2];
                    alpha += source[offset + 3];
                    count++;
                }

                var destination = (y * stride) + (x * 4);
                target[destination] = (byte)(blue / count);
                target[destination + 1] = (byte)(green / count);
                target[destination + 2] = (byte)(red / count);
                target[destination + 3] = (byte)(alpha / count);
            }
        }
    }

    private void DrawFallbackItems(Graphics graphics, DockMetrics metrics, ref int x, int y, bool interactive, int skip = 0, int? take = null)
    {
        var items = new[]
        {
            ("Messages", SystemGlyph.Messages, Color.FromArgb(255, 39, 215, 80)),
            ("Safari", SystemGlyph.Safari, Color.FromArgb(255, 0, 122, 255)),
            ("Music", SystemGlyph.Music, Color.FromArgb(255, 255, 45, 85)),
            ("Mail", SystemGlyph.Mail, Color.FromArgb(255, 0, 145, 255)),
            ("Files", SystemGlyph.Files, Color.FromArgb(255, 0, 122, 255)),
            ("Photos", SystemGlyph.Photos, Color.FromArgb(255, 255, 149, 0)),
            ("News", SystemGlyph.News, Color.FromArgb(255, 255, 59, 48)),
            ("Notes", SystemGlyph.Notes, Color.FromArgb(255, 255, 204, 0))
        };

        foreach (var item in items.Skip(skip).Take(take ?? items.Length))
        {
            var bounds = new Rectangle(x, y, metrics.IconSlot, metrics.IconSlot);
            var iconBounds = Centered(bounds, metrics.IconSize);
            DrawFallbackAppIcon(graphics, iconBounds, item.Item3, item.Item2);
            if (interactive)
            {
                _hitTargets.Add(new DockHitTarget(bounds, new DockItemViewModel(new AppEntry(item.Item1, item.Item1, AppKind.SystemAction), false), _hitTargets.Count));
            }

            x += metrics.IconSlot + metrics.ItemGap;
        }
    }

    private int DrawItems(Graphics graphics, IEnumerable<DockItemViewModel> items, DockMetrics metrics, ref int x, int y, bool pinned, ref int appIndex)
    {
        var count = 0;
        foreach (var item in items.Take(pinned ? 8 : 4))
        {
            DrawDropSlotIfNeeded(graphics, metrics, ref x, y, appIndex);
            var bounds = new Rectangle(x, y, metrics.IconSlot, metrics.IconSlot);
            var target = new DockHitTarget(bounds, item, _hitTargets.Count);
            _hitTargets.Add(target);
            DrawIcon(graphics, item, ApplyPressedVisual(graphics, Centered(bounds, metrics.IconSize), metrics, IsPressedVisual(target)));
            if (item.IsRunning || !pinned)
            {
                using var indicator = new SolidBrush(Color.FromArgb(255, 10, 132, 255));
                graphics.FillRoundedRectangle(indicator, new Rectangle(x + ((metrics.IconSlot - metrics.RunningIndicatorWidth) / 2), y + metrics.IconSlot - metrics.RunningIndicatorTopInset, metrics.RunningIndicatorWidth, metrics.RunningIndicatorHeight), metrics.RunningIndicatorHeight);
            }

            x += metrics.IconSlot + metrics.ItemGap;
            count++;
            appIndex++;
        }

        return count;
    }

    private void DrawIcon(Graphics graphics, DockItemViewModel item, Rectangle bounds)
    {
        var image = LoadIcon(item.IconPath);
        if (image is not null)
        {
            graphics.DrawImage(image, bounds);
            return;
        }

        DrawFallbackAppIcon(graphics, bounds, ColorFromName(item.DisplayName), GlyphFromName(item.DisplayName));
    }

    private static Rectangle ApplyPressedVisual(Graphics graphics, Rectangle bounds, DockMetrics metrics, bool isPressed)
    {
        if (!isPressed)
        {
            return bounds;
        }

        var glowBounds = Rectangle.Inflate(bounds, Scale(7, metrics.DpiScale), Scale(7, metrics.DpiScale));
        using var glowPath = RoundedRect(glowBounds, Math.Max(14, glowBounds.Width / 5));
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(150, 255, 255, 255),
            SurroundColors = [Color.FromArgb(0, 255, 255, 255)]
        };
        graphics.FillPath(glow, glowPath);
        return Rectangle.Inflate(bounds, -Scale(3, metrics.DpiScale), -Scale(3, metrics.DpiScale));
    }

    private static Rectangle Centered(Rectangle outer, int size)
    {
        return new Rectangle(outer.Left + ((outer.Width - size) / 2), outer.Top + ((outer.Height - size) / 2), size, size);
    }

    private static void DrawFallbackAppIcon(Graphics graphics, Rectangle bounds, Color color, SystemGlyph glyph)
    {
        using var path = RoundedRect(bounds, Math.Max(12, bounds.Width / 5));
        using var brush = new LinearGradientBrush(bounds, ControlPaint.Light(color, 0.24f), ControlPaint.Dark(color, 0.08f), 90f);
        using var pen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);
        DrawSystemGlyph(graphics, bounds, glyph);
    }

    private static void DrawHomeIcon(Graphics graphics, Rectangle bounds, DockMetrics metrics)
    {
        using var path = RoundedRect(bounds, Math.Max(12, bounds.Width / 5));
        using var brush = new LinearGradientBrush(
            bounds,
            Color.FromArgb(255, 255, 183, 77),
            Color.FromArgb(255, 255, 112, 67),
            90f);
        using var pen = new Pen(Color.FromArgb(96, 255, 255, 255), Math.Max(1f, metrics.DpiScale));
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);

        var unit = bounds.Width / 100f;
        using var roofBrush = new SolidBrush(Color.White);
        using var bodyBrush = new SolidBrush(Color.FromArgb(238, 255, 255, 255));
        var roof = new[]
        {
            new PointF(bounds.Left + (20 * unit), bounds.Top + (51 * unit)),
            new PointF(bounds.Left + (50 * unit), bounds.Top + (25 * unit)),
            new PointF(bounds.Left + (80 * unit), bounds.Top + (51 * unit)),
            new PointF(bounds.Left + (72 * unit), bounds.Top + (51 * unit)),
            new PointF(bounds.Left + (72 * unit), bounds.Top + (76 * unit)),
            new PointF(bounds.Left + (28 * unit), bounds.Top + (76 * unit)),
            new PointF(bounds.Left + (28 * unit), bounds.Top + (51 * unit))
        };
        graphics.FillPolygon(roofBrush, roof);
        using var doorPath = RoundedRect(
            Rectangle.Round(new RectangleF(bounds.Left + (43 * unit), bounds.Top + (55 * unit), 14 * unit, 21 * unit)),
            Math.Max(2, (int)(4 * unit)));
        using var doorBrush = new SolidBrush(Color.FromArgb(255, 255, 145, 48));
        graphics.FillPath(doorBrush, doorPath);
        using var bodyPen = new Pen(Color.FromArgb(56, 70, 38, 18), Math.Max(1f, metrics.DpiScale));
        graphics.DrawPolygon(bodyPen, roof);
    }

    private static void DrawSystemGlyph(Graphics graphics, Rectangle bounds, SystemGlyph glyph)
    {
        using var whiteBrush = new SolidBrush(Color.White);
        using var softBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
        using var whitePen = new Pen(Color.White, Math.Max(2f, bounds.Width * 0.055f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var darkPen = new Pen(Color.FromArgb(80, 30, 35, 44), Math.Max(1f, bounds.Width * 0.025f));
        var r = bounds;
        var unit = r.Width / 100f;

        switch (glyph)
        {
            case SystemGlyph.Messages:
                using (var bubble = RoundedRect(Rectangle.Round(new RectangleF(r.Left + (20 * unit), r.Top + (23 * unit), 60 * unit, 43 * unit)), (int)(18 * unit)))
                {
                    graphics.FillPath(whiteBrush, bubble);
                }
                var tail = new PointF[]
                {
                    new(r.Left + (42 * unit), r.Top + (62 * unit)),
                    new(r.Left + (34 * unit), r.Top + (76 * unit)),
                    new(r.Left + (55 * unit), r.Top + (64 * unit))
                };
                graphics.FillPolygon(whiteBrush, tail);
                break;
            case SystemGlyph.Safari:
                graphics.FillEllipse(whiteBrush, r.Left + (22 * unit), r.Top + (20 * unit), 56 * unit, 56 * unit);
                graphics.DrawEllipse(darkPen, r.Left + (31 * unit), r.Top + (29 * unit), 38 * unit, 38 * unit);
                using (var needle = new SolidBrush(Color.FromArgb(255, 255, 59, 48)))
                {
                    graphics.FillPolygon(needle, [new PointF(r.Left + (52 * unit), r.Top + (27 * unit)), new PointF(r.Left + (61 * unit), r.Top + (55 * unit)), new PointF(r.Left + (47 * unit), r.Top + (50 * unit))]);
                }
                break;
            case SystemGlyph.Music:
                graphics.DrawLine(whitePen, r.Left + (60 * unit), r.Top + (24 * unit), r.Left + (60 * unit), r.Top + (62 * unit));
                graphics.DrawLine(whitePen, r.Left + (60 * unit), r.Top + (25 * unit), r.Left + (35 * unit), r.Top + (30 * unit));
                graphics.FillEllipse(whiteBrush, r.Left + (27 * unit), r.Top + (58 * unit), 25 * unit, 20 * unit);
                break;
            case SystemGlyph.Mail:
                using (var envelope = RoundedRect(Rectangle.Round(new RectangleF(r.Left + (19 * unit), r.Top + (27 * unit), 62 * unit, 46 * unit)), (int)(8 * unit)))
                {
                    graphics.FillPath(softBrush, envelope);
                }
                graphics.DrawLine(darkPen, r.Left + (22 * unit), r.Top + (31 * unit), r.Left + (50 * unit), r.Top + (53 * unit));
                graphics.DrawLine(darkPen, r.Left + (78 * unit), r.Top + (31 * unit), r.Left + (50 * unit), r.Top + (53 * unit));
                break;
            case SystemGlyph.Files:
                using (var folderBack = RoundedRect(Rectangle.Round(new RectangleF(r.Left + (18 * unit), r.Top + (30 * unit), 64 * unit, 43 * unit)), (int)(8 * unit)))
                using (var tab = RoundedRect(Rectangle.Round(new RectangleF(r.Left + (22 * unit), r.Top + (24 * unit), 30 * unit, 16 * unit)), (int)(6 * unit)))
                using (var tabBrush = new SolidBrush(Color.FromArgb(255, 255, 222, 87)))
                {
                    graphics.FillPath(tabBrush, tab);
                    graphics.FillPath(whiteBrush, folderBack);
                }
                break;
            case SystemGlyph.Photos:
                var colors = new[]
                {
                    Color.FromArgb(255, 255, 59, 48), Color.FromArgb(255, 255, 149, 0), Color.FromArgb(255, 255, 204, 0),
                    Color.FromArgb(255, 52, 199, 89), Color.FromArgb(255, 90, 200, 250), Color.FromArgb(255, 0, 122, 255),
                    Color.FromArgb(255, 175, 82, 222), Color.FromArgb(255, 255, 45, 85)
                };
                for (var index = 0; index < colors.Length; index++)
                {
                    using var petal = new SolidBrush(colors[index]);
                    var angle = index * 45 * Math.PI / 180;
                    var cx = r.Left + (50 * unit) + (float)Math.Cos(angle) * 17 * unit;
                    var cy = r.Top + (50 * unit) + (float)Math.Sin(angle) * 17 * unit;
                    graphics.FillEllipse(petal, cx - (10 * unit), cy - (10 * unit), 20 * unit, 20 * unit);
                }
                graphics.FillEllipse(whiteBrush, r.Left + (42 * unit), r.Top + (42 * unit), 16 * unit, 16 * unit);
                break;
            case SystemGlyph.News:
                using (var font = new Font("Segoe UI", Math.Max(16, r.Width * 0.54f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.DrawString("N", font, whiteBrush, r, format);
                }
                break;
            case SystemGlyph.Notes:
                graphics.FillRectangle(whiteBrush, r.Left + (22 * unit), r.Top + (27 * unit), 56 * unit, 48 * unit);
                using (var yellow = new SolidBrush(Color.FromArgb(255, 255, 204, 0)))
                {
                    graphics.FillRectangle(yellow, r.Left + (22 * unit), r.Top + (27 * unit), 56 * unit, 15 * unit);
                }
                graphics.DrawLine(darkPen, r.Left + (31 * unit), r.Top + (52 * unit), r.Left + (69 * unit), r.Top + (52 * unit));
                graphics.DrawLine(darkPen, r.Left + (31 * unit), r.Top + (63 * unit), r.Left + (63 * unit), r.Top + (63 * unit));
                break;
            case SystemGlyph.Settings:
                graphics.DrawEllipse(whitePen, r.Left + (30 * unit), r.Top + (30 * unit), 40 * unit, 40 * unit);
                graphics.FillEllipse(whiteBrush, r.Left + (43 * unit), r.Top + (43 * unit), 14 * unit, 14 * unit);
                break;
        }
    }

    private static Color ColorFromName(string value)
    {
        var hash = value.Aggregate(17, (current, character) => (current * 31) + character);
        var palette = new[]
        {
            Color.FromArgb(255, 0, 122, 255),
            Color.FromArgb(255, 52, 199, 89),
            Color.FromArgb(255, 255, 149, 0),
            Color.FromArgb(255, 175, 82, 222),
            Color.FromArgb(255, 255, 59, 48),
            Color.FromArgb(255, 90, 200, 250)
        };
        return palette[Math.Abs(hash) % palette.Length];
    }

    private static SystemGlyph GlyphFromName(string value)
    {
        if (value.Contains("edge", StringComparison.OrdinalIgnoreCase) || value.Contains("browser", StringComparison.OrdinalIgnoreCase) || value.Contains("web", StringComparison.OrdinalIgnoreCase))
        {
            return SystemGlyph.Safari;
        }

        if (value.Contains("mail", StringComparison.OrdinalIgnoreCase) || value.Contains("outlook", StringComparison.OrdinalIgnoreCase))
        {
            return SystemGlyph.Mail;
        }

        if (value.Contains("explorer", StringComparison.OrdinalIgnoreCase) || value.Contains("file", StringComparison.OrdinalIgnoreCase))
        {
            return SystemGlyph.Files;
        }

        if (value.Contains("photo", StringComparison.OrdinalIgnoreCase))
        {
            return SystemGlyph.Photos;
        }

        if (value.Contains("music", StringComparison.OrdinalIgnoreCase) || value.Contains("media", StringComparison.OrdinalIgnoreCase))
        {
            return SystemGlyph.Music;
        }

        if (value.Contains("setting", StringComparison.OrdinalIgnoreCase))
        {
            return SystemGlyph.Settings;
        }

        if (value.Contains("note", StringComparison.OrdinalIgnoreCase))
        {
            return SystemGlyph.Notes;
        }

        return SystemGlyph.News;
    }

    private Image? LoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (_iconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var image = Image.FromFile(path);
            _iconCache[path] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void DrawSoftShadow(Graphics graphics, Rectangle surface)
    {
        for (var index = 0; index < 8; index++)
        {
            var alpha = 42 - (index * 4);
            var shadowRect = Rectangle.Inflate(surface, index * 3, index * 2);
            shadowRect.Offset(0, 6 + index);
            using var shadow = new SolidBrush(Color.FromArgb(Math.Max(6, alpha), 0, 0, 0));
            using var shadowPath = RoundedRect(shadowRect, Math.Max(18, surface.Height / 2) + index);
            graphics.FillPath(shadow, shadowPath);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ConfigureDwmBackdrop(IntPtr handle)
    {
        var useHostBackdrop = 1;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_USE_HOSTBACKDROPBRUSH, ref useHostBackdrop, sizeof(int));

        var backdropType = DWMSBT_TRANSIENTWINDOW;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

        var cornerPreference = DWMWCP_ROUND;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        var borderColor = DWMWA_COLOR_NONE;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

        EnableAcrylicAccent(handle);
    }

    private static void EnableAcrylicAccent(IntPtr handle)
    {
        var accent = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0x20,
            GradientColor = unchecked((int)0x66FFFFFF)
        };

        var accentSize = Marshal.SizeOf<AccentPolicy>();
        var accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = accentPtr,
                SizeOfData = accentSize
            };
            _ = NativeMethods.SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _visibilityTimer.Dispose();
            _animationTimer.Dispose();
            _blurredBackdrop?.Dispose();
            foreach (var image in _iconCache.Values)
            {
                image.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly DockItemViewModel HomeDockItem = new(new AppEntry("coredesk-home", "Home", AppKind.SystemAction), false);
    private static readonly Color TransparencyKeyColor = Color.FromArgb(1, 2, 3);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private sealed record DockMetrics(
        float DpiScale,
        int VisualItemCount,
        int IconSlot,
        int IconSize,
        int ItemGap,
        int SeparatorGap,
        int CornerRadius,
        int RunningIndicatorWidth,
        int RunningIndicatorHeight,
        int RunningIndicatorTopInset,
        int TopShadow,
        int DockHeight,
        int WindowWidth,
        int WindowHeight,
        int BottomInset,
        int ScreenInset,
        int SideShadow);

    private sealed record DockHitTarget(Rectangle Bounds, DockItemViewModel Item, int Index, bool IsHome = false);

    private enum SystemGlyph
    {
        Messages,
        Safari,
        Music,
        Mail,
        Files,
        Photos,
        News,
        Notes,
        Settings
    }

    private static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport("user32.dll")]
        public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint dwAffinity);

        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
