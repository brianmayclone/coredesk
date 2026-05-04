using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;
using CoreDesk.Application.ViewModels;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoreDesk_Dock;

public sealed class NativeDockForm : Form
{
    private readonly ShellViewModel _viewModel;
    private readonly IDiagnosticsService _diagnostics;
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _visibilityTimer = new();
    private readonly System.Windows.Forms.Timer _animationTimer = new();
    private readonly Dictionary<string, Image> _iconCache = [];
    private readonly List<DockHitTarget> _hitTargets = [];
    private bool _initialized;
    private bool _initializing;
    private bool _isAutoHidden;
    private bool _isAnimating;
    private int _visibleTop;
    private int _hiddenTop;
    private int _animationTargetTop;
    private DateTime? _foregroundOverlapSince;
    private DockHitTarget? _pressedTarget;
    private Point _mouseDownLocation;
    private bool _dragStarted;

    public NativeDockForm(ShellViewModel viewModel, IDiagnosticsService diagnostics)
    {
        _viewModel = viewModel;
        _diagnostics = diagnostics;

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
        ConfigureDwmBackdrop(Handle);
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
            _ = OpenDockItemAsync(target.Item);
        }

        _dragStarted = false;
    }

    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        drgevent.Effect = TryGetDraggedAppId(drgevent.Data, out _) ? DragDropEffects.Move : DragDropEffects.None;
        ShowDockAnimated();
    }

    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        drgevent.Effect = TryGetDraggedAppId(drgevent.Data, out _) ? DragDropEffects.Move : DragDropEffects.None;
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
            PositionDock();
            Invalidate();
        }
        catch (Exception exception)
        {
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
        if (_isAutoHidden && cursor.Y >= screen.Bottom - Scale(4, GetMetrics().DpiScale))
        {
            ShowDockAnimated();
            _foregroundOverlapSince = null;
            return;
        }

        if (!_isAutoHidden && IsForegroundWindowUnderDock())
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

    private bool IsForegroundWindowUnderDock()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == Handle)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == (uint)Environment.ProcessId)
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
        var visualCount = Math.Min(metrics.VisualItemCount, 8);
        var contentWidth = (visualCount * metrics.IconSlot) + (Math.Max(0, visualCount - 1) * metrics.ItemGap);
        var x = (Width - contentWidth) / 2;
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

        using var glass = new LinearGradientBrush(
            dockSurface,
            Color.FromArgb(172, 74, 36, 52),
            Color.FromArgb(138, 36, 18, 28),
            90f);
        using var sheen = new LinearGradientBrush(
            dockSurface,
            Color.FromArgb(112, 255, 255, 255),
            Color.FromArgb(8, 255, 255, 255),
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
        if (!_initialized || _viewModel.PinnedDockItems.Count == 0)
        {
            DrawFallbackItems(graphics, metrics, ref x, y, interactive: true);
            return;
        }

        var drawnCount = DrawItems(graphics, _viewModel.PinnedDockItems, metrics, ref x, y, pinned: true);

        if (_viewModel.RunningDockItems.Count > 0)
        {
            x += metrics.SeparatorGap;
        }

        drawnCount += DrawItems(graphics, _viewModel.RunningDockItems, metrics, ref x, y, pinned: false);

        var missingCount = Math.Max(0, metrics.VisualItemCount - drawnCount);
        if (missingCount > 0)
        {
            DrawFallbackItems(graphics, metrics, ref x, y, interactive: false, skip: drawnCount, take: missingCount);
        }
    }

    private int GetDockItemCount()
    {
        if (!_initialized || _viewModel.PinnedDockItems.Count == 0)
        {
            return 8;
        }

        return Math.Clamp(_viewModel.PinnedDockItems.Count, 1, 8)
            + Math.Clamp(_viewModel.RunningDockItems.Count, 0, 4);
    }

    private DockMetrics GetMetrics()
    {
        var scale = Math.Clamp(DeviceDpi / 96f, 1f, 2.5f);
        var iconSlot = Scale(86, scale);
        var iconSize = Scale(72, scale);
        var itemGap = Scale(13, scale);
        var sidePadding = Scale(26, scale);
        var itemCount = Math.Max(8, GetDockItemCount());
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

    private int DrawItems(Graphics graphics, IEnumerable<DockItemViewModel> items, DockMetrics metrics, ref int x, int y, bool pinned)
    {
        var count = 0;
        foreach (var item in items.Take(pinned ? 8 : 4))
        {
            var bounds = new Rectangle(x, y, metrics.IconSlot, metrics.IconSlot);
            _hitTargets.Add(new DockHitTarget(bounds, item, _hitTargets.Count));
            DrawIcon(graphics, item, Centered(bounds, metrics.IconSize));
            if (item.IsRunning || !pinned)
            {
                using var indicator = new SolidBrush(Color.FromArgb(255, 10, 132, 255));
                graphics.FillRoundedRectangle(indicator, new Rectangle(x + ((metrics.IconSlot - metrics.RunningIndicatorWidth) / 2), y + metrics.IconSlot - metrics.RunningIndicatorTopInset, metrics.RunningIndicatorWidth, metrics.RunningIndicatorHeight), metrics.RunningIndicatorHeight);
            }

            x += metrics.IconSlot + metrics.ItemGap;
            count++;
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
            foreach (var image in _iconCache.Values)
            {
                image.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly Color TransparencyKeyColor = Color.FromArgb(1, 2, 3);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const uint SWP_SHOWWINDOW = 0x0040;
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

    private sealed record DockHitTarget(Rectangle Bounds, DockItemViewModel Item, int Index);

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
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

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
