using CoreDesk.Application.ViewModels;
using Microsoft.Graphics.Canvas.Effects;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Specialized;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace CoreDesk_App;

public sealed class CompositionDockHost : IDisposable
{
    private readonly ShellViewModel _viewModel;
    private readonly List<DockHitTarget> _hitTargets = [];
    private readonly Dictionary<string, CompositionSurfaceBrush> _iconBrushes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, System.Drawing.Image> _gdiIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly WndProc _wndProc;
    private readonly WndProc _iconWndProc;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _rebuildTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _visibilityTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _animationTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pressAnimationTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _layerRepairTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dropAnimationTimer;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private ContainerVisual? _root;
    private DockDropTarget? _dropTarget;
    private string? _lastVisualSignature;
    private nint _dispatcherQueueController;
    private nint _hwnd;
    private nint _iconHwnd;
    private bool _disposed;
    private bool _initialized;
    private bool _forceScheduledRebuild;
    private bool _isAutoHidden;
    private bool _isAnimating;
    private bool _homeMode = true;
    private bool _oleInitialized;
    private bool _dragStarted;
    private bool _isDragHoveringDock;
    private DockHitTarget? _pressedTarget;
    private string? _pressedVisualId;
    private Windows.Foundation.Point _mouseDownPoint;
    private DateTime _pressAnimationStartedAt = DateTime.MinValue;
    private DateTime? _foregroundOverlapSince;
    private float _dropSlotProgress;
    private float _dropSlotTargetProgress;
    private int _dropTargetIndex = -1;
    private int _visibleTop;
    private int _hiddenTop;
    private int _animationTargetTop;
    private int _layerRepairTicksRemaining;
    private int _left;
    private int _top;
    private int _width;
    private int _height;

    public CompositionDockHost(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        _wndProc = WindowProcedure;
        _iconWndProc = IconWindowProcedure;
        _viewModel.PinnedDockItems.CollectionChanged += OnDockItemsChanged;
        _viewModel.RunningDockItems.CollectionChanged += OnDockItemsChanged;
    }

    public async void ShowDock(bool homeMode = false)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _homeMode = homeMode;
            await App.EnsureShellReadyAsync();
            EnsureWindow();
            PositionWindow();
            if (_homeMode)
            {
                ShowDockAnimated();
            }

            RebuildVisualTree(force: _lastVisualSignature is null);
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            ShowWindow(_iconHwnd, SW_SHOWNOACTIVATE);
            EnsureDockLayerOrder();
            App.Services.Diagnostics.Info($"Composition dock shown. Hwnd={_hwnd}; Bounds={_width}x{_height}; HomeMode={_homeMode}; Pinned={_viewModel.PinnedDockItems.Count}; Running={_viewModel.RunningDockItems.Count}.");
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, "Composition dock failed to show.");
        }
    }

    public void Close()
    {
        if (_iconHwnd != 0)
        {
            RevokeDockDropTarget();
            DestroyWindow(_iconHwnd);
            _iconHwnd = 0;
        }

        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PinnedDockItems.CollectionChanged -= OnDockItemsChanged;
        _viewModel.RunningDockItems.CollectionChanged -= OnDockItemsChanged;
        _ = ReleaseCapture();
        _rebuildTimer?.Stop();
        _visibilityTimer?.Stop();
        _animationTimer?.Stop();
        _pressAnimationTimer?.Stop();
        _layerRepairTimer?.Stop();
        _dropAnimationTimer?.Stop();
        foreach (var brush in _iconBrushes.Values)
        {
            brush.Dispose();
        }

        foreach (var image in _gdiIconCache.Values)
        {
            image.Dispose();
        }

        _iconBrushes.Clear();
        _gdiIconCache.Clear();
        _target?.Dispose();
        _compositor?.Dispose();
        Close();
        if (_oleInitialized)
        {
            OleUninitialize();
            _oleInitialized = false;
        }
    }

    private void EnsureWindow()
    {
        if (_initialized)
        {
            return;
        }

        var instance = GetModuleHandle(null);
        var className = $"CoreDeskCompositionDock-{Environment.ProcessId}";
        var windowClass = new WindowClass
        {
            Size = Marshal.SizeOf<WindowClass>(),
            Instance = instance,
            ClassName = className,
            WindowProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            Cursor = LoadCursor(0, IDC_ARROW)
        };

        _ = RegisterClassEx(ref windowClass);
        var iconClassName = $"{className}-Icons";
        var iconWindowClass = windowClass;
        iconWindowClass.ClassName = iconClassName;
        iconWindowClass.WindowProc = Marshal.GetFunctionPointerForDelegate(_iconWndProc);
        _ = RegisterClassEx(ref iconWindowClass);

        _hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP,
            className,
            "CoreDesk Dock",
            WS_POPUP,
            0,
            0,
            1,
            1,
            0,
            0,
            instance,
            0);
        if (_hwnd == 0)
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        _iconHwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_LAYERED,
            iconClassName,
            "CoreDesk Dock Icons",
            WS_POPUP,
            0,
            0,
            1,
            1,
            _hwnd,
            0,
            instance,
            0);
        if (_iconHwnd == 0)
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        EnsureWindowsCompositionDispatcherQueue();
        _compositor = new Compositor();
        _target = CreateDesktopWindowTarget(_compositor, _hwnd);
        _root = _compositor.CreateContainerVisual();
        _target.Root = _root;
        RegisterDockDropTarget();
        _initialized = true;
        StartVisibilityMonitor();
    }

    private void PositionWindow()
    {
        var metrics = GetMetrics();
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        _width = Math.Min(metrics.WindowWidth, screenWidth - (metrics.ScreenInset * 2));
        _height = metrics.WindowHeight;
        _left = (screenWidth - _width) / 2;
        _visibleTop = screenHeight - _height - metrics.BottomInset;
        _hiddenTop = screenHeight - Math.Max(Scale(7, metrics.DpiScale), 4);
        if (!_isAnimating)
        {
            _top = _isAutoHidden ? _hiddenTop : _visibleTop;
        }

        SetDockWindowPosition(_top);
    }

    private DockMetrics GetMetrics()
    {
        var dpi = _hwnd == 0 ? 96u : GetDpiForWindow(_hwnd);
        var scale = Math.Clamp(dpi / 96f, 1f, 2.5f);
        var iconSlot = Scale(78, scale);
        var itemGap = Scale(12, scale);
        var sidePadding = Scale(22, scale);
        var sideShadow = Scale(28, scale);
        var pinnedItemCount = Math.Clamp(_viewModel.PinnedDockItems.Count, 0, 8);
        var runningItemCount = Math.Clamp(_viewModel.RunningDockItems.Count, 0, 4);
        var itemCount = 2 + pinnedItemCount + runningItemCount;
        var separatorGap = runningItemCount > 0 ? Scale(18, scale) : 0;
        var dropSlotExtent = ScaleDropSlot(iconSlot + itemGap);
        var contentWidth = (itemCount * iconSlot) + (Math.Max(0, itemCount - 1) * itemGap) + separatorGap + dropSlotExtent;
        var dockWidth = contentWidth + (sidePadding * 2);
        return new DockMetrics(
            scale,
            iconSlot,
            Scale(64, scale),
            itemGap,
            separatorGap,
            Math.Max(Scale(38, scale), Scale(64, scale) / 2),
            Scale(26, scale),
            Math.Max(Scale(92, scale), iconSlot + Scale(16, scale)),
            dockWidth + (sideShadow * 2),
            Math.Max(Scale(132, scale), iconSlot + Scale(54, scale)),
            Scale(16, scale),
            Scale(34, scale),
            sideShadow,
            pinnedItemCount,
            runningItemCount,
            dropSlotExtent);
    }

    private void RebuildVisualTree(bool force = false)
    {
        if (_compositor is null || _root is null || _width <= 0 || _height <= 0)
        {
            return;
        }

        var signature = BuildVisualSignature();
        if (!force && string.Equals(_lastVisualSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastVisualSignature = signature;
        _hitTargets.Clear();
        _root.Children.RemoveAll();
        _root.Size = new Vector2(_width, _height);
        var metrics = GetMetrics();
        var dockRect = new Windows.Foundation.Rect(metrics.SideShadow, metrics.TopShadow, _width - (metrics.SideShadow * 2), metrics.DockHeight);
        var dockSize = new Vector2((float)dockRect.Width, (float)dockRect.Height);
        var dockOffset = new Vector3((float)dockRect.X, (float)dockRect.Y, 0);

        for (var index = 5; index >= 1; index--)
        {
            var spread = index * 4f;
            var alpha = (byte)Math.Clamp(20 - (index * 3), 4, 18);
            var shadow = CreateRoundedShape(
                dockSize + new Vector2(spread * 2f, spread * 1.3f),
                metrics.CornerRadius + spread,
                Windows.UI.Color.FromArgb(alpha, 0, 0, 0));
            shadow.Offset = dockOffset + new Vector3(-spread, 5 + (index * 1.4f), 0);
            _root.Children.InsertAtTop(shadow);
        }

        var glass = _compositor.CreateSpriteVisual();
        glass.Size = dockSize;
        glass.Offset = dockOffset;
        glass.Brush = CreateGlassBrush();
        glass.Clip = CreateRoundedClip(dockSize, metrics.CornerRadius);
        _root.Children.InsertAtTop(glass);

        var stroke = CreateRoundedStroke(dockSize, metrics.CornerRadius, Windows.UI.Color.FromArgb(124, 255, 255, 255), Math.Max(1f, metrics.DpiScale));
        stroke.Offset = dockOffset;
        _root.Children.InsertAtTop(stroke);

        var visualIndex = 0;
        var contentWidth = GetDockContentWidth(metrics);
        var x = (_width - contentWidth) / 2f;
        var y = (float)dockRect.Y + (((float)dockRect.Height - metrics.IconSlot) / 2f);
        AddHomeIcon(x, y, metrics, GetSlotVisualId(visualIndex++));
        x += metrics.IconSlot + metrics.ItemGap;
        AddAppDrawerIcon(x, y, metrics, GetSlotVisualId(visualIndex++));
        x += metrics.IconSlot + metrics.ItemGap;

        var appIndex = 0;
        ApplyDropSlotSpacing(ref x, appIndex, metrics);
        foreach (var item in _viewModel.PinnedDockItems.Take(8))
        {
            AddDockItem(item, x, y, metrics, GetSlotVisualId(visualIndex++));
            x += metrics.IconSlot + metrics.ItemGap;
            appIndex++;
            ApplyDropSlotSpacing(ref x, appIndex, metrics);
        }

        if (_viewModel.RunningDockItems.Count > 0)
        {
            var separator = _compositor.CreateSpriteVisual();
            separator.Size = new Vector2(Math.Max(1f, metrics.DpiScale), Scale(50, metrics.DpiScale));
            separator.Offset = new Vector3(x + (metrics.SeparatorGap / 2f), y + ((metrics.IconSlot - separator.Size.Y) / 2f), 0);
            separator.Brush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(110, 255, 255, 255));
            _root.Children.InsertAtTop(separator);
            x += metrics.SeparatorGap;
        }

        foreach (var item in _viewModel.RunningDockItems.Take(4))
        {
            AddDockItem(item, x, y, metrics, GetSlotVisualId(visualIndex++));
            x += metrics.IconSlot + metrics.ItemGap;
        }

        RenderIconOverlay(metrics, dockRect);
    }

    private void AddHomeIcon(float x, float y, DockMetrics metrics, string visualId)
    {
        _hitTargets.Add(new DockHitTarget(new Windows.Foundation.Rect(x, y, metrics.IconSlot, metrics.IconSlot), null, DockSystemAction.Home, visualId));
    }

    private void AddAppDrawerIcon(float x, float y, DockMetrics metrics, string visualId)
    {
        _hitTargets.Add(new DockHitTarget(new Windows.Foundation.Rect(x, y, metrics.IconSlot, metrics.IconSlot), null, DockSystemAction.AppDrawer, visualId));
    }

    private void AddDockItem(DockItemViewModel item, float x, float y, DockMetrics metrics, string visualId)
    {
        _hitTargets.Add(new DockHitTarget(new Windows.Foundation.Rect(x, y, metrics.IconSlot, metrics.IconSlot), item, DockSystemAction.None, visualId));
    }

    private static int GetDockContentWidth(DockMetrics metrics)
    {
        var itemCount = 2 + Math.Max(0, metrics.PinnedItemCount) + Math.Max(0, metrics.RunningItemCount);
        return (itemCount * metrics.IconSlot)
            + (Math.Max(0, itemCount - 1) * metrics.ItemGap)
            + (metrics.RunningItemCount > 0 ? metrics.SeparatorGap : 0)
            + metrics.DropSlotExtent;
    }

    private void ApplyDropSlotSpacing(ref float x, int appIndex, DockMetrics metrics)
    {
        if (metrics.DropSlotExtent <= 0 || !_isDragHoveringDock || _dropTargetIndex != appIndex)
        {
            return;
        }

        x += metrics.DropSlotExtent;
    }

    private void DrawDropSlotOverlayIfNeeded(System.Drawing.Graphics graphics, DockMetrics metrics, ref int x, int y, int appIndex)
    {
        if (metrics.DropSlotExtent <= 0 || !_isDragHoveringDock || _dropTargetIndex != appIndex)
        {
            return;
        }

        var progress = EaseOutCubic(_dropSlotProgress);
        var slotX = x + ((metrics.DropSlotExtent - metrics.IconSlot) / 2);
        var outer = new System.Drawing.Rectangle(slotX, y, metrics.IconSlot, metrics.IconSlot);
        var size = Math.Max(1, (int)Math.Round(metrics.IconSize * Math.Clamp(progress, 0.25f, 1f)));
        var slot = Centered(outer, size);
        using var path = RoundedRect(slot, Math.Max(12, slot.Width / 5));
        using var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb((int)Math.Round(62 * progress), 255, 255, 255));
        using var stroke = new System.Drawing.Pen(System.Drawing.Color.FromArgb((int)Math.Round(152 * progress), 255, 255, 255), Math.Max(1f, metrics.DpiScale))
        {
            DashStyle = DashStyle.Dash
        };
        graphics.FillPath(fill, path);
        graphics.DrawPath(stroke, path);
        x += metrics.DropSlotExtent;
    }

    private void RenderIconOverlay(DockMetrics metrics, Windows.Foundation.Rect dockRect)
    {
        if (_iconHwnd == 0 || _width <= 0 || _height <= 0)
        {
            return;
        }

        using var bitmap = new System.Drawing.Bitmap(_width, _height, PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(System.Drawing.Color.Transparent);

            var visualIndex = 0;
            var contentWidth = GetDockContentWidth(metrics);
            var x = (int)Math.Round((_width - contentWidth) / 2f);
            var y = (int)Math.Round(dockRect.Y + ((dockRect.Height - metrics.IconSlot) / 2f));
            var homeBounds = Centered(new System.Drawing.Rectangle(x, y, metrics.IconSlot, metrics.IconSlot), metrics.IconSize);
            DrawHomeIconOverlay(graphics, ApplyTapVisual(graphics, homeBounds, GetSlotVisualId(visualIndex++), metrics), metrics);
            x += metrics.IconSlot + metrics.ItemGap;
            var drawerBounds = Centered(new System.Drawing.Rectangle(x, y, metrics.IconSlot, metrics.IconSlot), metrics.IconSize);
            DrawAppDrawerIconOverlay(graphics, ApplyTapVisual(graphics, drawerBounds, GetSlotVisualId(visualIndex++), metrics), metrics);
            x += metrics.IconSlot + metrics.ItemGap;

            var appIndex = 0;
            DrawDropSlotOverlayIfNeeded(graphics, metrics, ref x, y, appIndex);
            foreach (var item in _viewModel.PinnedDockItems.Take(8))
            {
                DrawDockItemOverlay(graphics, item, new System.Drawing.Rectangle(x, y, metrics.IconSlot, metrics.IconSlot), metrics, GetSlotVisualId(visualIndex++), showIndicator: item.IsRunning);
                x += metrics.IconSlot + metrics.ItemGap;
                appIndex++;
                DrawDropSlotOverlayIfNeeded(graphics, metrics, ref x, y, appIndex);
            }

            if (_viewModel.RunningDockItems.Count > 0)
            {
                using var separator = new System.Drawing.Pen(System.Drawing.Color.FromArgb(110, 255, 255, 255), Math.Max(1f, metrics.DpiScale));
                var separatorX = x + (metrics.SeparatorGap / 2);
                graphics.DrawLine(separator, separatorX, y + ((metrics.IconSlot - Scale(50, metrics.DpiScale)) / 2), separatorX, y + ((metrics.IconSlot + Scale(50, metrics.DpiScale)) / 2));
                x += metrics.SeparatorGap;
            }

            foreach (var item in _viewModel.RunningDockItems.Take(4))
            {
                DrawDockItemOverlay(graphics, item, new System.Drawing.Rectangle(x, y, metrics.IconSlot, metrics.IconSlot), metrics, GetSlotVisualId(visualIndex++), showIndicator: true);
                x += metrics.IconSlot + metrics.ItemGap;
            }
        }

        UpdateLayeredWindowFromBitmap(_iconHwnd, bitmap);
    }

    private void DrawDockItemOverlay(System.Drawing.Graphics graphics, DockItemViewModel item, System.Drawing.Rectangle slot, DockMetrics metrics, string visualId, bool showIndicator)
    {
        var bounds = ApplyTapVisual(graphics, Centered(slot, metrics.IconSize), visualId, metrics);
        var image = LoadGdiIcon(item.IconPath);
        if (image is not null)
        {
            graphics.DrawImage(image, bounds);
        }
        else
        {
            DrawFallbackIconOverlay(graphics, bounds, item.DisplayName);
        }

        if (showIndicator)
        {
            using var indicator = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 10, 132, 255));
            using var path = RoundedRect(new System.Drawing.Rectangle(
                slot.Left + ((slot.Width - Scale(8, metrics.DpiScale)) / 2),
                slot.Top + slot.Height - Scale(9, metrics.DpiScale),
                Scale(8, metrics.DpiScale),
                Scale(3, metrics.DpiScale)),
                Scale(2, metrics.DpiScale));
            graphics.FillPath(indicator, path);
        }
    }

    private System.Drawing.Rectangle ApplyTapVisual(System.Drawing.Graphics graphics, System.Drawing.Rectangle bounds, string visualId, DockMetrics metrics)
    {
        var progress = GetPressProgress(visualId);
        if (progress is null)
        {
            return bounds;
        }

        var eased = EaseOutCubic(progress.Value);
        var scale = progress.Value < 0.34f
            ? 1f - (0.075f * (progress.Value / 0.34f))
            : 0.925f + (0.075f * ((progress.Value - 0.34f) / 0.66f));
        var glowAlpha = (int)Math.Round(118 * (1f - eased));
        var glowBounds = System.Drawing.Rectangle.Inflate(bounds, Scale(9, metrics.DpiScale), Scale(9, metrics.DpiScale));
        using (var glowPath = RoundedRect(glowBounds, Math.Max(14, glowBounds.Width / 5)))
        using (var glow = new PathGradientBrush(glowPath))
        {
            glow.CenterColor = System.Drawing.Color.FromArgb(Math.Clamp(glowAlpha, 0, 118), 255, 255, 255);
            glow.SurroundColors = [System.Drawing.Color.FromArgb(0, 255, 255, 255)];
            graphics.FillPath(glow, glowPath);
        }

        return ScaleAroundCenter(bounds, scale);
    }

    private float? GetPressProgress(string visualId)
    {
        if (!string.Equals(_pressedVisualId, visualId, StringComparison.Ordinal)
            || _pressAnimationStartedAt == DateTime.MinValue)
        {
            return null;
        }

        var elapsed = (float)(DateTime.UtcNow - _pressAnimationStartedAt).TotalMilliseconds / PressAnimationDurationMs;
        return elapsed >= 1f ? null : Math.Clamp(elapsed, 0f, 1f);
    }

    private static float EaseOutCubic(float value)
    {
        var inverse = 1f - value;
        return 1f - (inverse * inverse * inverse);
    }

    private static System.Drawing.Rectangle ScaleAroundCenter(System.Drawing.Rectangle bounds, float scale)
    {
        var width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        return new System.Drawing.Rectangle(
            bounds.Left + ((bounds.Width - width) / 2),
            bounds.Top + ((bounds.Height - height) / 2),
            width,
            height);
    }

    private System.Drawing.Image? LoadGdiIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        if (_gdiIconCache.TryGetValue(iconPath, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = File.OpenRead(iconPath);
            using var loaded = System.Drawing.Image.FromStream(stream);
            var image = new System.Drawing.Bitmap(loaded);
            _gdiIconCache[iconPath] = image;
            return image;
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, $"Failed to load dock icon '{iconPath}'.");
            return null;
        }
    }

    private static void DrawHomeIconOverlay(System.Drawing.Graphics graphics, System.Drawing.Rectangle bounds, DockMetrics metrics)
    {
        using var path = RoundedRect(bounds, Math.Max(12, bounds.Width / 5));
        using var brush = new LinearGradientBrush(bounds, System.Drawing.Color.FromArgb(255, 255, 166, 74), System.Drawing.Color.FromArgb(255, 255, 106, 64), 90f);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(78, 255, 255, 255), Math.Max(1f, metrics.DpiScale));
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);

        using var line = new System.Drawing.Pen(System.Drawing.Color.FromArgb(242, 255, 255, 255), Math.Max(3f, metrics.DpiScale * 3f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLine(line, bounds.Left + (bounds.Width * 0.24f), bounds.Top + (bounds.Height * 0.52f), bounds.Left + (bounds.Width * 0.50f), bounds.Top + (bounds.Height * 0.26f));
        graphics.DrawLine(line, bounds.Left + (bounds.Width * 0.50f), bounds.Top + (bounds.Height * 0.26f), bounds.Left + (bounds.Width * 0.76f), bounds.Top + (bounds.Height * 0.52f));
        graphics.DrawLine(line, bounds.Left + (bounds.Width * 0.31f), bounds.Top + (bounds.Height * 0.50f), bounds.Left + (bounds.Width * 0.31f), bounds.Top + (bounds.Height * 0.76f));
        graphics.DrawLine(line, bounds.Left + (bounds.Width * 0.69f), bounds.Top + (bounds.Height * 0.50f), bounds.Left + (bounds.Width * 0.69f), bounds.Top + (bounds.Height * 0.76f));
        graphics.DrawLine(line, bounds.Left + (bounds.Width * 0.31f), bounds.Top + (bounds.Height * 0.76f), bounds.Left + (bounds.Width * 0.69f), bounds.Top + (bounds.Height * 0.76f));
    }

    private static void DrawAppDrawerIconOverlay(System.Drawing.Graphics graphics, System.Drawing.Rectangle bounds, DockMetrics metrics)
    {
        using var path = RoundedRect(bounds, Math.Max(12, bounds.Width / 5));
        using var brush = new LinearGradientBrush(bounds, System.Drawing.Color.FromArgb(255, 0, 122, 255), System.Drawing.Color.FromArgb(255, 90, 200, 250), 90f);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(78, 255, 255, 255), Math.Max(1f, metrics.DpiScale));
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);

        using var dotBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(244, 255, 255, 255));
        var dotSize = Math.Max(4, (int)Math.Round(bounds.Width * 0.13));
        var gap = Math.Max(4, (int)Math.Round(bounds.Width * 0.13));
        var cluster = (dotSize * 3) + (gap * 2);
        var startX = bounds.Left + ((bounds.Width - cluster) / 2);
        var startY = bounds.Top + ((bounds.Height - cluster) / 2);
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                graphics.FillEllipse(dotBrush, startX + (column * (dotSize + gap)), startY + (row * (dotSize + gap)), dotSize, dotSize);
            }
        }
    }

    private static void DrawFallbackIconOverlay(System.Drawing.Graphics graphics, System.Drawing.Rectangle bounds, string name)
    {
        var color = ColorFromName(name);
        using var path = RoundedRect(bounds, Math.Max(12, bounds.Width / 5));
        using var brush = new LinearGradientBrush(bounds, ControlPaintLight(color), ControlPaintDark(color), 90f);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(80, 255, 255, 255), 1f);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);

        var initial = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpperInvariant();
        using var font = new System.Drawing.Font("Segoe UI", Math.Max(16, bounds.Width * 0.45f), System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(238, 255, 255, 255));
        using var format = new System.Drawing.StringFormat
        {
            Alignment = System.Drawing.StringAlignment.Center,
            LineAlignment = System.Drawing.StringAlignment.Center
        };
        graphics.DrawString(initial, font, textBrush, bounds, format);
    }

    private static System.Drawing.Color ColorFromName(string name)
    {
        var colors = new[]
        {
            System.Drawing.Color.FromArgb(255, 0, 122, 255),
            System.Drawing.Color.FromArgb(255, 52, 199, 89),
            System.Drawing.Color.FromArgb(255, 255, 149, 0),
            System.Drawing.Color.FromArgb(255, 175, 82, 222),
            System.Drawing.Color.FromArgb(255, 255, 59, 48),
            System.Drawing.Color.FromArgb(255, 90, 200, 250)
        };
        var hash = name.Aggregate(17, (current, character) => (current * 31) + character);
        return colors[Math.Abs(hash) % colors.Length];
    }

    private static System.Drawing.Color ControlPaintLight(System.Drawing.Color color)
    {
        return System.Drawing.Color.FromArgb(color.A, Math.Min(255, color.R + 48), Math.Min(255, color.G + 48), Math.Min(255, color.B + 48));
    }

    private static System.Drawing.Color ControlPaintDark(System.Drawing.Color color)
    {
        return System.Drawing.Color.FromArgb(color.A, Math.Max(0, color.R - 28), Math.Max(0, color.G - 28), Math.Max(0, color.B - 28));
    }

    private static GraphicsPath RoundedRect(System.Drawing.Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static System.Drawing.Rectangle Centered(System.Drawing.Rectangle outer, int size)
    {
        return new System.Drawing.Rectangle(outer.Left + ((outer.Width - size) / 2), outer.Top + ((outer.Height - size) / 2), size, size);
    }

    private void AddGlyphLine(float x1, float y1, float x2, float y2, DockMetrics metrics)
    {
        if (_compositor is null || _root is null)
        {
            return;
        }

        var line = _compositor.CreateLineGeometry();
        line.Start = new Vector2(x1, y1);
        line.End = new Vector2(x2, y2);
        var shape = _compositor.CreateSpriteShape(line);
        shape.StrokeBrush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(242, 255, 255, 255));
        shape.StrokeThickness = Math.Max(3f, metrics.DpiScale * 3f);
        var visual = _compositor.CreateShapeVisual();
        visual.Size = new Vector2(_width, _height);
        visual.Shapes.Add(shape);
        _root.Children.InsertAtTop(visual);
    }

    private SpriteVisual CreateGradientVisual(Vector2 size, Vector3 offset, (float Offset, Windows.UI.Color Color)[] stops, float cornerRadius)
    {
        var brush = _compositor!.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0, 0);
        brush.EndPoint = new Vector2(0, 1);
        foreach (var stop in stops)
        {
            brush.ColorStops.Add(_compositor.CreateColorGradientStop(stop.Offset, stop.Color));
        }

        var visual = _compositor.CreateSpriteVisual();
        visual.Size = size;
        visual.Offset = offset;
        visual.Brush = brush;
        visual.Clip = CreateRoundedClip(size, cornerRadius);
        return visual;
    }

    private ShapeVisual CreateRoundedShape(Vector2 size, float cornerRadius, Windows.UI.Color color)
    {
        var geometry = _compositor!.CreateRoundedRectangleGeometry();
        geometry.Size = size;
        geometry.CornerRadius = new Vector2(cornerRadius);
        var shape = _compositor.CreateSpriteShape(geometry);
        shape.FillBrush = _compositor.CreateColorBrush(color);
        var visual = _compositor.CreateShapeVisual();
        visual.Size = size;
        visual.Shapes.Add(shape);
        return visual;
    }

    private ShapeVisual CreateRoundedStroke(Vector2 size, float cornerRadius, Windows.UI.Color color, float thickness)
    {
        var geometry = _compositor!.CreateRoundedRectangleGeometry();
        geometry.Size = size - new Vector2(thickness);
        geometry.Offset = new Vector2(thickness / 2f);
        geometry.CornerRadius = new Vector2(cornerRadius);
        var shape = _compositor.CreateSpriteShape(geometry);
        shape.StrokeBrush = _compositor.CreateColorBrush(color);
        shape.StrokeThickness = thickness;
        var visual = _compositor.CreateShapeVisual();
        visual.Size = size;
        visual.Shapes.Add(shape);
        return visual;
    }

    private CompositionGeometricClip CreateRoundedClip(Vector2 size, float cornerRadius)
    {
        var geometry = _compositor!.CreateRoundedRectangleGeometry();
        geometry.Size = size;
        geometry.CornerRadius = new Vector2(cornerRadius);
        return _compositor.CreateGeometricClip(geometry);
    }

    private CompositionBrush CreateGlassBrush()
    {
        var backdrop = _compositor!.CreateBackdropBrush();
        var effect = new ColorMatrixEffect
        {
            Name = "dockBrightness",
            ColorMatrix = new Matrix5x4
            {
                M11 = 1f,
                M22 = 1f,
                M33 = 1f,
                M44 = 1f,
                M51 = 0.04f,
                M52 = 0.04f,
                M53 = 0.04f
            },
            Source = new ContrastEffect
            {
                Name = "dockContrast",
                Contrast = 0.14f,
                Source = new SaturationEffect
                {
                    Name = "dockSaturation",
                    Saturation = 1.42f,
                    Source = new GaussianBlurEffect
                    {
                        Name = "dockBlur",
                        BlurAmount = 30f,
                        BorderMode = EffectBorderMode.Hard,
                        Optimization = EffectOptimization.Balanced,
                        Source = new CompositionEffectSourceParameter("backdrop")
                    }
                }
            }
        };
        var factory = _compositor.CreateEffectFactory(effect);
        var brush = factory.CreateBrush();
        brush.SetSourceParameter("backdrop", backdrop);
        return brush;
    }

    private CompositionSurfaceBrush? GetIconBrush(string? iconPath)
    {
        if (_compositor is null || string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        if (_iconBrushes.TryGetValue(iconPath, out var cached))
        {
            return cached;
        }

        return null;
    }

    private CompositionBrush CreateFallbackIconBrush(string name)
    {
        var colors = new[]
        {
            Windows.UI.Color.FromArgb(255, 0, 122, 255),
            Windows.UI.Color.FromArgb(255, 52, 199, 89),
            Windows.UI.Color.FromArgb(255, 255, 149, 0),
            Windows.UI.Color.FromArgb(255, 175, 82, 222),
            Windows.UI.Color.FromArgb(255, 255, 59, 48),
            Windows.UI.Color.FromArgb(255, 90, 200, 250)
        };
        var hash = name.Aggregate(17, (current, character) => (current * 31) + character);
        return _compositor!.CreateColorBrush(colors[Math.Abs(hash) % colors.Length]);
    }

    private async void ActivateTarget(DockHitTarget target)
    {
        if (target.SystemAction == DockSystemAction.Home)
        {
            App.ShowMainShell();
            MinimizeApplicationWindows();
            return;
        }

        if (target.SystemAction == DockSystemAction.AppDrawer)
        {
            App.ShowMainShell(openDrawer: true);
            return;
        }

        if (target.Item is not null)
        {
            await _viewModel.OpenDockItemCommand.ExecuteAsync(target.Item);
            _homeMode = false;
            EnsureDockLayerOrder();
            ScheduleDockLayerRepair();
        }
    }

    private void UpdateLayeredWindowFromBitmap(nint hwnd, System.Drawing.Bitmap bitmap)
    {
        var screenDc = GetDC(0);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);
        try
        {
            var destination = new NativePoint { X = _left, Y = _top };
            var source = new NativePoint { X = 0, Y = 0 };
            var size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
            var blend = new BlendFunction
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };
            _ = UpdateLayeredWindow(hwnd, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            _ = SelectObject(memoryDc, oldBitmap);
            _ = DeleteObject(bitmapHandle);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(0, screenDc);
        }
    }

    private void StartPressAnimation(nint inputHwnd, Windows.Foundation.Point point)
    {
        var target = _hitTargets.FirstOrDefault(candidate => candidate.Bounds.Contains(point));
        if (target is null)
        {
            return;
        }

        _pressedTarget = target;
        _pressedVisualId = target.VisualId;
        _mouseDownPoint = point;
        _dragStarted = false;
        _pressAnimationStartedAt = DateTime.UtcNow;
        RedrawIconOverlay();
        _ = SetCapture(inputHwnd);

        if (_pressAnimationTimer is null)
        {
            _pressAnimationTimer = App.DispatcherQueue.CreateTimer();
            _pressAnimationTimer.Interval = TimeSpan.FromMilliseconds(15);
            _pressAnimationTimer.IsRepeating = true;
            _pressAnimationTimer.Tick += (_, _) => StepPressAnimation();
        }

        _pressAnimationTimer.Start();
    }

    private void CompletePress(Windows.Foundation.Point point)
    {
        if (_dragStarted)
        {
            _pressedTarget = null;
            _dragStarted = false;
            _ = ReleaseCapture();
            return;
        }

        var target = _hitTargets.FirstOrDefault(candidate => candidate.Bounds.Contains(point));
        var pressedTarget = _pressedTarget;
        _pressedTarget = null;
        _ = ReleaseCapture();

        if (target is not null
            && pressedTarget is not null
            && string.Equals(target.VisualId, pressedTarget.VisualId, StringComparison.Ordinal))
        {
            ActivateTarget(target);
            return;
        }

        if (target is not null)
        {
            ActivateTarget(target);
        }
    }

    private void StepPressAnimation()
    {
        if ((DateTime.UtcNow - _pressAnimationStartedAt).TotalMilliseconds >= PressAnimationDurationMs)
        {
            _pressAnimationTimer?.Stop();
            _pressedVisualId = null;
            _pressAnimationStartedAt = DateTime.MinValue;
        }

        RedrawIconOverlay();
    }

    private void TryStartDockItemDrag(Windows.Foundation.Point point, nuint wParam)
    {
        if (_pressedTarget is not { SystemAction: DockSystemAction.None, Item: not null } pressedTarget || _dragStarted || (wParam & MK_LBUTTON) == 0)
        {
            return;
        }

        var dx = Math.Abs(point.X - _mouseDownPoint.X);
        var dy = Math.Abs(point.Y - _mouseDownPoint.Y);
        if (dx < GetSystemMetrics(SM_CXDRAG) && dy < GetSystemMetrics(SM_CYDRAG))
        {
            return;
        }

        _dragStarted = true;
        _pressedVisualId = null;
        _pressAnimationTimer?.Stop();
        RedrawIconOverlay();
        _ = ReleaseCapture();

        try
        {
            var dataObject = new TextDataObject($"coredesk-dock-app:{pressedTarget.Item.App.Id}");
            var dropSource = new DockDropSource();
            _ = DoDragDrop(dataObject, dropSource, DROPEFFECT_MOVE, out _);
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, $"Dock drag failed for app '{pressedTarget.Item.App.Id}'.");
        }
        finally
        {
            _dragStarted = false;
            _pressedTarget = null;
            _pressedVisualId = null;
            RedrawIconOverlay();
            EnsureDockLayerOrder();
        }
    }

    private void RedrawIconOverlay()
    {
        if (_iconHwnd == 0 || _width <= 0 || _height <= 0)
        {
            return;
        }

        var metrics = GetMetrics();
        var dockRect = new Windows.Foundation.Rect(metrics.SideShadow, metrics.TopShadow, _width - (metrics.SideShadow * 2), metrics.DockHeight);
        RenderIconOverlay(metrics, dockRect);
    }

    private void RegisterDockDropTarget()
    {
        if (_iconHwnd == 0 || _dropTarget is not null)
        {
            return;
        }

        var oleResult = OleInitialize(0);
        _oleInitialized = oleResult >= 0 || oleResult == S_FALSE;
        if (oleResult < 0 && oleResult != S_FALSE)
        {
            App.Services.Diagnostics.Info($"Dock OLE initialization failed: 0x{oleResult:X8}.");
            return;
        }

        _dropTarget = new DockDropTarget(this);
        var registerResult = RegisterDragDrop(_iconHwnd, _dropTarget);
        if (registerResult < 0 && registerResult != DRAGDROP_E_ALREADYREGISTERED)
        {
            _dropTarget = null;
            App.Services.Diagnostics.Info($"Dock drag/drop registration failed: 0x{registerResult:X8}.");
        }
    }

    private void RevokeDockDropTarget()
    {
        if (_dropTarget is null || _iconHwnd == 0)
        {
            _dropTarget = null;
            return;
        }

        _ = RevokeDragDrop(_iconHwnd);
        _dropTarget = null;
    }

    private bool PreviewDockDrop(IDataObject dataObject, NativePoint screenPoint)
    {
        if (!TryGetDraggedAppId(dataObject, out _))
        {
            SetDockDropVisual(false, -1);
            return false;
        }

        SetDockDropVisual(true, GetDockTargetIndex(ScreenToDockPoint(screenPoint)));
        ShowDockAnimated();
        return true;
    }

    private bool UpdateDockDrop(NativePoint screenPoint)
    {
        if (!_isDragHoveringDock)
        {
            return false;
        }

        SetDockDropVisual(true, GetDockTargetIndex(ScreenToDockPoint(screenPoint)));
        return true;
    }

    private async void DropDockData(IDataObject dataObject, NativePoint screenPoint)
    {
        if (!TryGetDraggedAppId(dataObject, out var appId))
        {
            SetDockDropVisual(false, -1);
            return;
        }

        var targetIndex = GetDockTargetIndex(ScreenToDockPoint(screenPoint));
        SetDockDropVisual(false, -1);
        try
        {
            await _viewModel.MoveDockItemAsync(appId, targetIndex);
            ScheduleVisualRefresh(force: true);
            EnsureDockLayerOrder();
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, $"Dock drop failed for app '{appId}'.");
        }
    }

    private void SetDockDropVisual(bool isHovering, int targetIndex)
    {
        var nextTargetProgress = isHovering ? 1f : 0f;
        var changed = _isDragHoveringDock != isHovering || _dropTargetIndex != targetIndex || Math.Abs(_dropSlotTargetProgress - nextTargetProgress) > 0.001f;
        _isDragHoveringDock = isHovering || _dropSlotProgress > 0.001f;
        if (targetIndex >= 0)
        {
            _dropTargetIndex = targetIndex;
        }

        _dropSlotTargetProgress = nextTargetProgress;
        if (changed)
        {
            PositionWindow();
            RebuildVisualTree(force: true);
        }

        StartDropSlotAnimation();
    }

    private void StartDropSlotAnimation()
    {
        if (_dropAnimationTimer is null)
        {
            _dropAnimationTimer = App.DispatcherQueue.CreateTimer();
            _dropAnimationTimer.Interval = TimeSpan.FromMilliseconds(15);
            _dropAnimationTimer.IsRepeating = true;
            _dropAnimationTimer.Tick += (_, _) => StepDropSlotAnimation();
        }

        _dropAnimationTimer.Start();
    }

    private void StepDropSlotAnimation()
    {
        var delta = _dropSlotTargetProgress - _dropSlotProgress;
        if (Math.Abs(delta) <= 0.025f)
        {
            _dropSlotProgress = _dropSlotTargetProgress;
            if (_dropSlotProgress <= 0.001f)
            {
                _isDragHoveringDock = false;
                _dropTargetIndex = -1;
                _dropAnimationTimer?.Stop();
            }
        }
        else
        {
            _dropSlotProgress += delta * 0.32f;
        }

        PositionWindow();
        RebuildVisualTree(force: true);
    }

    private Windows.Foundation.Point ScreenToDockPoint(NativePoint screenPoint)
    {
        var point = screenPoint;
        _ = ScreenToClient(_iconHwnd, ref point);
        return new Windows.Foundation.Point(point.X, point.Y);
    }

    private int GetDockTargetIndex(Windows.Foundation.Point point)
    {
        var metrics = GetMetrics();
        var contentWidth = GetDockContentWidth(metrics);
        var x = ((_width - contentWidth) / 2f) + ((metrics.IconSlot + metrics.ItemGap) * 2);
        var pinnedCount = Math.Clamp(_viewModel.PinnedDockItems.Count, 0, 8);

        for (var index = 0; index <= pinnedCount; index++)
        {
            if (metrics.DropSlotExtent > 0 && _dropTargetIndex == index)
            {
                if (point.X < x + metrics.DropSlotExtent)
                {
                    return index;
                }

                x += metrics.DropSlotExtent;
            }

            if (index == pinnedCount)
            {
                break;
            }

            if (point.X < x + (metrics.IconSlot / 2f))
            {
                return index;
            }

            x += metrics.IconSlot + metrics.ItemGap;
        }

        return pinnedCount;
    }

    private static bool TryGetDraggedAppId(IDataObject dataObject, out string appId)
    {
        appId = string.Empty;
        if (!TryReadTextData(dataObject, out var text))
        {
            return false;
        }

        const string appPrefix = "coredesk-app:";
        const string dockAppPrefix = "coredesk-dock-app:";
        if (text.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase))
        {
            appId = text[appPrefix.Length..].Trim();
        }
        else if (text.StartsWith(dockAppPrefix, StringComparison.OrdinalIgnoreCase))
        {
            appId = text[dockAppPrefix.Length..].Trim();
        }

        return !string.IsNullOrWhiteSpace(appId);
    }

    private static bool TryReadTextData(IDataObject dataObject, out string text)
    {
        text = string.Empty;
        var format = CreateUnicodeTextFormat();
        if (dataObject.QueryGetData(ref format) != S_OK)
        {
            return false;
        }

        dataObject.GetData(ref format, out var medium);
        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == 0)
            {
                return false;
            }

            var locked = GlobalLock(medium.unionmember);
            if (locked == 0)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(locked) ?? string.Empty;
                return !string.IsNullOrWhiteSpace(text);
            }
            finally
            {
                _ = GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static string GetSlotVisualId(int index)
    {
        return $"slot:{index}";
    }

    private void StartVisibilityMonitor()
    {
        if (_visibilityTimer is not null)
        {
            return;
        }

        _visibilityTimer = App.DispatcherQueue.CreateTimer();
        _visibilityTimer.Interval = TimeSpan.FromMilliseconds(250);
        _visibilityTimer.IsRepeating = true;
        _visibilityTimer.Tick += (_, _) => MonitorAutoHide();
        _visibilityTimer.Start();
    }

    private void MonitorAutoHide()
    {
        if (_disposed || _hwnd == 0)
        {
            return;
        }

        if (IsPointerInDockInteractionZone())
        {
            _foregroundOverlapSince = null;
            ShowDockAnimated();
            return;
        }

        var hasOverlap = HasVisibleWindowInDockWorkArea();
        if (!hasOverlap)
        {
            _foregroundOverlapSince = null;
            ShowDockAnimated();
            return;
        }

        // Home mode keeps the dock visible only while the desktop area is actually clear.
        // If another app overlaps the dock zone, the dock behaves like the iPad dock and yields.
        if (_isAutoHidden)
        {
            return;
        }

        _foregroundOverlapSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _foregroundOverlapSince.Value >= TimeSpan.FromSeconds(5))
        {
            HideDockAnimated();
        }
    }

    private void HideDockAnimated()
    {
        if (_isAutoHidden && _animationTargetTop == _hiddenTop)
        {
            return;
        }

        _isAutoHidden = true;
        StartDockAnimation(_hiddenTop);
    }

    private void ShowDockAnimated()
    {
        if (!_isAutoHidden && _animationTargetTop == _visibleTop && !_isAnimating)
        {
            return;
        }

        _isAutoHidden = false;
        StartDockAnimation(_visibleTop);
    }

    private void StartDockAnimation(int targetTop)
    {
        _animationTargetTop = targetTop;
        if (_hwnd == 0 || _iconHwnd == 0 || _top == targetTop)
        {
            _isAnimating = false;
            SetDockWindowPosition(targetTop);
            return;
        }

        if (_animationTimer is null)
        {
            _animationTimer = App.DispatcherQueue.CreateTimer();
            _animationTimer.Interval = TimeSpan.FromMilliseconds(15);
            _animationTimer.IsRepeating = true;
            _animationTimer.Tick += (_, _) => StepDockAnimation();
        }

        _isAnimating = true;
        _animationTimer.Start();
    }

    private void StepDockAnimation()
    {
        var distance = _animationTargetTop - _top;
        if (Math.Abs(distance) <= 1)
        {
            _animationTimer?.Stop();
            _isAnimating = false;
            SetDockWindowPosition(_animationTargetTop);
            EnsureDockLayerOrder();
            return;
        }

        var step = Math.Sign(distance) * Math.Max(1, Math.Abs(distance) / 4);
        SetDockWindowPosition(_top + step);
    }

    private void SetDockWindowPosition(int top)
    {
        _top = top;
        var moveFlags = SWP_NOACTIVATE | SWP_NOZORDER;
        if (_hwnd != 0)
        {
            SetWindowPos(_hwnd, 0, _left, _top, _width, _height, moveFlags);
        }

        if (_iconHwnd != 0)
        {
            SetWindowPos(_iconHwnd, 0, _left, _top, _width, _height, moveFlags);
        }
    }

    private void EnsureDockLayerOrder()
    {
        if (_hwnd != 0)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        if (_iconHwnd != 0)
        {
            SetWindowPos(_iconHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private void ScheduleDockLayerRepair()
    {
        if (_layerRepairTimer is null)
        {
            _layerRepairTimer = App.DispatcherQueue.CreateTimer();
            _layerRepairTimer.Interval = TimeSpan.FromMilliseconds(180);
            _layerRepairTimer.IsRepeating = true;
            _layerRepairTimer.Tick += (_, _) =>
            {
                EnsureDockLayerOrder();
                _layerRepairTicksRemaining--;
                if (_layerRepairTicksRemaining <= 0)
                {
                    _layerRepairTimer.Stop();
                }
            };
        }

        _layerRepairTicksRemaining = 4;
        _layerRepairTimer.Stop();
        _layerRepairTimer.Start();
    }

    private void MinimizeApplicationWindows()
    {
        var minimized = 0;
        EnumWindows((handle, lParam) =>
        {
            if (ShouldIgnoreApplicationWindow(handle))
            {
                return true;
            }

            _ = ShowWindow(handle, SW_FORCEMINIMIZE);
            minimized++;
            return true;
        }, 0);
        App.Services.Diagnostics.Info($"Home dock button minimized {minimized} windows.");
    }

    private bool IsPointerInDockInteractionZone()
    {
        if (!GetCursorPos(out var point))
        {
            return false;
        }

        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        var horizontalInset = Math.Max(Scale(160, GetMetrics().DpiScale), _width / 5);
        var withinHorizontalZone = point.X >= Math.Max(0, _left - horizontalInset)
            && point.X <= Math.Min(screenWidth, _left + _width + horizontalInset);
        if (!withinHorizontalZone)
        {
            return false;
        }

        var revealEdgeHeight = Math.Max(4, Scale(5, GetMetrics().DpiScale));
        if (point.Y >= screenHeight - revealEdgeHeight)
        {
            return true;
        }

        var hoverTop = Math.Min(_top, _visibleTop) - Scale(10, GetMetrics().DpiScale);
        var hoverBottom = Math.Max(_top + _height, _visibleTop + _height) + Scale(4, GetMetrics().DpiScale);
        return point.Y >= hoverTop && point.Y <= hoverBottom;
    }

    private bool HasVisibleWindowInDockWorkArea()
    {
        var metrics = GetMetrics();
        var dockWorkArea = new NativeRect
        {
            Left = _left + metrics.SideShadow,
            Top = _visibleTop + metrics.TopShadow,
            Right = _left + _width - metrics.SideShadow,
            Bottom = _visibleTop + metrics.TopShadow + metrics.DockHeight
        };
        var hasOverlap = false;
        EnumWindows((handle, _) =>
        {
            if (ShouldIgnoreOverlapWindow(handle))
            {
                return true;
            }

            var windowRect = GetEffectiveWindowRect(handle);
            if (windowRect.HasValue && Intersects(dockWorkArea, windowRect.Value))
            {
                hasOverlap = true;
                return false;
            }

            return true;
        }, 0);
        return hasOverlap;
    }

    private bool ShouldIgnoreOverlapWindow(nint handle)
    {
        return ShouldIgnoreApplicationWindow(handle);
    }

    private bool ShouldIgnoreApplicationWindow(nint handle)
    {
        if (handle == 0 || handle == _hwnd || handle == _iconHwnd || !IsWindowVisible(handle) || IsIconic(handle))
        {
            return true;
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId == Environment.ProcessId)
        {
            return true;
        }

        var exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
        {
            return true;
        }

        var className = GetWindowClassName(handle);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return true;
        }

        var cloaked = 0;
        if (DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out cloaked, Marshal.SizeOf<int>()) >= 0 && cloaked != 0)
        {
            return true;
        }

        return false;
    }

    private static NativeRect? GetEffectiveWindowRect(nint handle)
    {
        if (DwmGetWindowAttribute(handle, DWMWA_EXTENDED_FRAME_BOUNDS, out NativeRect frameBounds, Marshal.SizeOf<NativeRect>()) >= 0
            && frameBounds.Right > frameBounds.Left
            && frameBounds.Bottom > frameBounds.Top)
        {
            return frameBounds;
        }

        return GetWindowRect(handle, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top
            ? rect
            : null;
    }

    private static string GetWindowClassName(nint handle)
    {
        var builder = new StringBuilder(256);
        _ = GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool Intersects(NativeRect left, NativeRect right)
    {
        return left.Left < right.Right
            && left.Right > right.Left
            && left.Top < right.Bottom
            && left.Bottom > right.Top;
    }

    private nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_NCHITTEST:
                return HTCLIENT;
            case WM_LBUTTONDOWN:
                StartPressAnimation(hwnd, GetPointFromLParam(lParam));
                return 0;
            case WM_LBUTTONUP:
                CompletePress(GetPointFromLParam(lParam));
                return 0;
            case WM_POINTERDOWN:
                if (TryGetPointerPoint(wParam, out var pressedPoint))
                {
                    StartPressAnimation(hwnd, pressedPoint);
                    if (_isAutoHidden)
                    {
                        ShowDockAnimated();
                    }

                    return 0;
                }

                break;
            case WM_POINTERUP:
                if (TryGetPointerPoint(wParam, out var pointerPoint))
                {
                    CompletePress(pointerPoint);
                    return 0;
                }

                break;
            case WM_MOUSEMOVE:
                TryStartDockItemDrag(GetPointFromLParam(lParam), wParam);
                if (_isAutoHidden)
                {
                    ShowDockAnimated();
                }

                break;
            case WM_POINTERUPDATE:
                if (TryGetPointerPoint(wParam, out var updatePoint))
                {
                    TryStartDockItemDrag(updatePoint, (nuint)MK_LBUTTON);
                }

                if (_isAutoHidden)
                {
                    ShowDockAnimated();
                }

                break;
            case WM_DPICHANGED:
            case WM_DISPLAYCHANGE:
                PositionWindow();
                RebuildVisualTree(force: true);
                return 0;
            case WM_DESTROY:
                _target?.Dispose();
                _target = null;
                _root = null;
                _lastVisualSignature = null;
                _hwnd = 0;
                return 0;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private nint IconWindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_NCHITTEST:
                return HTCLIENT;
            case WM_LBUTTONDOWN:
                StartPressAnimation(hwnd, GetPointFromLParam(lParam));
                return 0;
            case WM_LBUTTONUP:
                CompletePress(GetPointFromLParam(lParam));
                return 0;
            case WM_POINTERDOWN:
                if (TryGetPointerPointForWindow(hwnd, wParam, out var pressedPoint))
                {
                    StartPressAnimation(hwnd, pressedPoint);
                    if (_isAutoHidden)
                    {
                        ShowDockAnimated();
                    }

                    return 0;
                }

                break;
            case WM_POINTERUP:
                if (TryGetPointerPointForWindow(hwnd, wParam, out var pointerPoint))
                {
                    CompletePress(pointerPoint);
                    return 0;
                }

                break;
            case WM_MOUSEMOVE:
                TryStartDockItemDrag(GetPointFromLParam(lParam), wParam);
                if (_isAutoHidden)
                {
                    ShowDockAnimated();
                }

                break;
            case WM_POINTERUPDATE:
                if (TryGetPointerPointForWindow(hwnd, wParam, out var updatePoint))
                {
                    TryStartDockItemDrag(updatePoint, (nuint)MK_LBUTTON);
                }

                if (_isAutoHidden)
                {
                    ShowDockAnimated();
                }

                break;
            case WM_DESTROY:
                if (hwnd == _iconHwnd)
                {
                    _iconHwnd = 0;
                }

                return 0;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void OnDockItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_hwnd == 0)
        {
            return;
        }

        ScheduleVisualRefresh();
    }

    private void ScheduleVisualRefresh(bool force = false)
    {
        if (_disposed || _hwnd == 0)
        {
            return;
        }

        _forceScheduledRebuild |= force;
        if (_rebuildTimer is null)
        {
            _rebuildTimer = App.DispatcherQueue.CreateTimer();
            _rebuildTimer.Interval = TimeSpan.FromMilliseconds(120);
            _rebuildTimer.IsRepeating = false;
            _rebuildTimer.Tick += (_, _) =>
            {
                var shouldForce = _forceScheduledRebuild;
                _forceScheduledRebuild = false;
                PositionWindow();
                RebuildVisualTree(shouldForce);
            };
        }

        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private string BuildVisualSignature()
    {
        return string.Join('|',
            _homeMode ? "home" : "apps",
            _width,
            _height,
            _dropTargetIndex,
            Math.Round(_dropSlotProgress, 2),
            string.Join(';', _viewModel.PinnedDockItems.Take(8).Select(CreateItemSignature)),
            string.Join(';', _viewModel.RunningDockItems.Take(4).Select(CreateItemSignature)));
    }

    private static string CreateItemSignature(DockItemViewModel item)
    {
        return string.Join(',',
            item.App.Id,
            item.DisplayName,
            item.IconPath,
            item.IsRunning,
            item.WindowTitle);
    }

    private void HandleClientClick(Windows.Foundation.Point point)
    {
        var target = _hitTargets.FirstOrDefault(candidate => candidate.Bounds.Contains(point));
        if (target is not null)
        {
            ActivateTarget(target);
        }
    }

    private static Windows.Foundation.Point GetPointFromLParam(nint lParam)
    {
        var value = lParam.ToInt64();
        var x = unchecked((short)(value & 0xFFFF));
        var y = unchecked((short)((value >> 16) & 0xFFFF));
        return new Windows.Foundation.Point(x, y);
    }

    private bool TryGetPointerPoint(nuint wParam, out Windows.Foundation.Point point)
    {
        return TryGetPointerPointForWindow(_hwnd, wParam, out point);
    }

    private bool TryGetPointerPointForWindow(nint hwnd, nuint wParam, out Windows.Foundation.Point point)
    {
        var pointerId = (uint)(wParam & 0xFFFF);
        if (GetPointerInfo(pointerId, out var info))
        {
            var nativePoint = info.PixelLocation;
            _ = ScreenToClient(hwnd, ref nativePoint);
            point = new Windows.Foundation.Point(nativePoint.X, nativePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    private static Windows.Foundation.Rect Centered(float x, float y, int outerSize, int innerSize)
    {
        return new Windows.Foundation.Rect(
            x + ((outerSize - innerSize) / 2f),
            y + ((outerSize - innerSize) / 2f),
            innerSize,
            innerSize);
    }

    private static DesktopWindowTarget CreateDesktopWindowTarget(Compositor compositor, nint hwnd)
    {
        var compositorUnknown = Marshal.GetIUnknownForObject(compositor);
        nint interopUnknown = 0;
        try
        {
            var interfaceId = typeof(ICompositorDesktopInterop).GUID;
            var queryResult = Marshal.QueryInterface(compositorUnknown, in interfaceId, out interopUnknown);
            if (queryResult < 0)
            {
                Marshal.ThrowExceptionForHR(queryResult);
            }

            var interop = (ICompositorDesktopInterop)Marshal.GetTypedObjectForIUnknown(interopUnknown, typeof(ICompositorDesktopInterop));
            interop.CreateDesktopWindowTarget(hwnd, true, out var targetUnknown);
            try
            {
                return WinRT.MarshalInspectable<DesktopWindowTarget>.FromAbi(targetUnknown);
            }
            finally
            {
                if (targetUnknown != 0)
                {
                    Marshal.Release(targetUnknown);
                }
            }
        }
        finally
        {
            if (interopUnknown != 0)
            {
                Marshal.Release(interopUnknown);
            }

            Marshal.Release(compositorUnknown);
        }
    }

    private void EnsureWindowsCompositionDispatcherQueue()
    {
        if (_dispatcherQueueController != 0)
        {
            return;
        }

        try
        {
            if (global::Windows.System.DispatcherQueue.GetForCurrentThread() is not null)
            {
                return;
            }
        }
        catch
        {
            // Older Windows composition interop will create the required queue below.
        }

        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = DQTYPE_THREAD_CURRENT,
            ApartmentType = DQTAT_COM_STA
        };
        var result = CreateDispatcherQueueController(options, out _dispatcherQueueController);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static int Scale(int value, float scale) => Math.Max(1, (int)Math.Round(value * scale));

    private int ScaleDropSlot(int fullExtent)
    {
        if (_dropSlotProgress <= 0.001f)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Round(fullExtent * EaseOutCubic(Math.Clamp(_dropSlotProgress, 0f, 1f))));
    }

    private sealed record DockMetrics(
        float DpiScale,
        int IconSlot,
        int IconSize,
        int ItemGap,
        int SeparatorGap,
        int CornerRadius,
        int TopShadow,
        int DockHeight,
        int WindowWidth,
        int WindowHeight,
        int BottomInset,
        int ScreenInset,
        int SideShadow,
        int PinnedItemCount,
        int RunningItemCount,
        int DropSlotExtent);

    private sealed record DockHitTarget(Windows.Foundation.Rect Bounds, DockItemViewModel? Item, DockSystemAction SystemAction, string VisualId);

    private enum DockSystemAction
    {
        None,
        Home,
        AppDrawer
    }

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwndTarget, bool isTopmost, out nint target);
    }

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTarget
    {
        void DragEnter(IDataObject dataObject, int keyState, NativePoint point, ref int effect);

        void DragOver(int keyState, NativePoint point, ref int effect);

        void DragLeave();

        void Drop(IDataObject dataObject, int keyState, NativePoint point, ref int effect);
    }

    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag(bool escapePressed, int keyState);

        [PreserveSig]
        int GiveFeedback(int effect);
    }

    [ComVisible(true)]
    private sealed class DockDropTarget(CompositionDockHost host) : IDropTarget
    {
        public void DragEnter(IDataObject dataObject, int keyState, NativePoint point, ref int effect)
        {
            effect = host.PreviewDockDrop(dataObject, point) ? DROPEFFECT_MOVE : DROPEFFECT_NONE;
        }

        public void DragOver(int keyState, NativePoint point, ref int effect)
        {
            effect = host.UpdateDockDrop(point) ? DROPEFFECT_MOVE : DROPEFFECT_NONE;
        }

        public void DragLeave()
        {
            host.SetDockDropVisual(false, -1);
        }

        public void Drop(IDataObject dataObject, int keyState, NativePoint point, ref int effect)
        {
            effect = DROPEFFECT_NONE;
            if (host.PreviewDockDrop(dataObject, point))
            {
                host.DropDockData(dataObject, point);
                effect = DROPEFFECT_MOVE;
            }
        }
    }

    [ComVisible(true)]
    private sealed class DockDropSource : IDropSource
    {
        public int QueryContinueDrag(bool escapePressed, int keyState)
        {
            if (escapePressed)
            {
                return DRAGDROP_S_CANCEL;
            }

            return (keyState & MK_LBUTTON) == 0 ? DRAGDROP_S_DROP : S_OK;
        }

        public int GiveFeedback(int effect)
        {
            return DRAGDROP_S_USEDEFAULTCURSORS;
        }
    }

    [ComVisible(true)]
    private sealed class TextDataObject(string text) : IDataObject
    {
        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            if (!IsUnicodeTextFormat(ref format))
            {
                Marshal.ThrowExceptionForHR(DV_E_FORMATETC);
            }

            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            var handle = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)bytes.Length);
            if (handle == 0)
            {
                Marshal.ThrowExceptionForHR(E_OUTOFMEMORY);
            }

            var locked = GlobalLock(handle);
            if (locked == 0)
            {
                _ = GlobalFree(handle);
                Marshal.ThrowExceptionForHR(E_OUTOFMEMORY);
            }

            try
            {
                Marshal.Copy(bytes, 0, locked, bytes.Length);
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }

            medium = new STGMEDIUM
            {
                tymed = TYMED.TYMED_HGLOBAL,
                unionmember = handle,
                pUnkForRelease = null
            };
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
        {
            Marshal.ThrowExceptionForHR(E_NOTIMPL);
        }

        public int QueryGetData(ref FORMATETC format)
        {
            return IsUnicodeTextFormat(ref format) ? S_OK : DV_E_FORMATETC;
        }

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            return DATA_S_SAMEFORMATETC;
        }

        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
        {
            Marshal.ThrowExceptionForHR(E_NOTIMPL);
        }

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            Marshal.ThrowExceptionForHR(E_NOTIMPL);
            return null!;
        }

        public int DAdvise(ref FORMATETC format, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            connection = 0;
            return OLE_E_ADVISENOTSUPPORTED;
        }

        public void DUnadvise(int connection)
        {
            Marshal.ThrowExceptionForHR(OLE_E_ADVISENOTSUPPORTED);
        }

        public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
        {
            enumAdvise = null!;
            return OLE_E_ADVISENOTSUPPORTED;
        }
    }

    private static FORMATETC CreateUnicodeTextFormat()
    {
        return new FORMATETC
        {
            cfFormat = CF_UNICODETEXT,
            ptd = 0,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL
        };
    }

    private static bool IsUnicodeTextFormat(ref FORMATETC format)
    {
        return format.cfFormat == CF_UNICODETEXT
            && (format.tymed & TYMED.TYMED_HGLOBAL) != 0;
    }

    private delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public int Size;
        public uint Style;
        public nint WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public uint PointerType;
        public uint PointerId;
        public uint FrameId;
        public uint PointerFlags;
        public nint SourceDevice;
        public nint TargetWindow;
        public NativePoint PixelLocation;
        public NativePoint HimetricLocation;
        public NativePoint PixelLocationRaw;
        public NativePoint HimetricLocationRaw;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public uint ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    private static readonly nint HWND_TOPMOST = new(-1);
    private static readonly nint IDC_ARROW = new(32512);
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int GWL_EXSTYLE = -20;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_FORCEMINIMIZE = 11;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_CXDRAG = 68;
    private const int SM_CYDRAG = 69;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_POINTERUPDATE = 0x0245;
    private const uint WM_POINTERDOWN = 0x0246;
    private const uint WM_POINTERUP = 0x0247;
    private const uint WM_DPICHANGED = 0x02E0;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;
    private const int PressAnimationDurationMs = 220;
    private const int DQTYPE_THREAD_CURRENT = 2;
    private const int DQTAT_COM_STA = 2;
    private const short CF_UNICODETEXT = 13;
    private const int MK_LBUTTON = 0x0001;
    private const int DROPEFFECT_NONE = 0;
    private const int DROPEFFECT_MOVE = 2;
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
    private const int DV_E_FORMATETC = unchecked((int)0x80040064);
    private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
    private const int DRAGDROP_E_ALREADYREGISTERED = unchecked((int)0x80040101);
    private const int DRAGDROP_S_DROP = 0x00040100;
    private const int DRAGDROP_S_CANCEL = 0x00040101;
    private const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
    private const int DATA_S_SAMEFORMATETC = 0x00040130;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;
    private static readonly nint HTCLIENT = new(1);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(nint hwnd, nint destinationDeviceContext, ref NativePoint destinationPoint, ref NativeSize size, nint sourceDeviceContext, ref NativePoint sourcePoint, int colorKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint graphicsObject);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetPointerInfo(uint pointerId, out PointerInfo pointerInfo);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect value, int size);

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint dispatcherQueueController);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(nint reserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(nint hwnd, IDropTarget dropTarget);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(nint hwnd);

    [DllImport("ole32.dll")]
    private static extern int DoDragDrop(IDataObject dataObject, IDropSource dropSource, int allowedEffects, out int effect);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint handle);
}
