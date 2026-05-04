using CoreDesk.Application.ViewModels;
using Microsoft.Graphics.Canvas.Effects;
using System.Collections.Specialized;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace CoreDesk_App;

public sealed class CompositionDockHost : IDisposable
{
    private readonly ShellViewModel _viewModel;
    private readonly List<DockHitTarget> _hitTargets = [];
    private readonly Dictionary<string, CompositionSurfaceBrush> _iconBrushes = new(StringComparer.OrdinalIgnoreCase);
    private readonly WndProc _wndProc;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private ContainerVisual? _root;
    private nint _dispatcherQueueController;
    private nint _hwnd;
    private bool _disposed;
    private bool _initialized;
    private bool _homeMode = true;
    private int _width;
    private int _height;

    public CompositionDockHost(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        _wndProc = WindowProcedure;
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
            RebuildVisualTree();
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            App.Services.Diagnostics.Info($"Composition dock shown. Hwnd={_hwnd}; Bounds={_width}x{_height}; HomeMode={_homeMode}; Pinned={_viewModel.PinnedDockItems.Count}; Running={_viewModel.RunningDockItems.Count}.");
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, "Composition dock failed to show.");
        }
    }

    public void Close()
    {
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
        foreach (var brush in _iconBrushes.Values)
        {
            brush.Dispose();
        }

        _iconBrushes.Clear();
        _target?.Dispose();
        _compositor?.Dispose();
        Close();
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

        EnsureWindowsCompositionDispatcherQueue();
        _compositor = new Compositor();
        _target = CreateDesktopWindowTarget(_compositor, _hwnd);
        _root = _compositor.CreateContainerVisual();
        _target.Root = _root;
        _initialized = true;
    }

    private void PositionWindow()
    {
        var metrics = GetMetrics();
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        _width = Math.Min(metrics.WindowWidth, screenWidth - (metrics.ScreenInset * 2));
        _height = metrics.WindowHeight;
        var x = (screenWidth - _width) / 2;
        var y = screenHeight - _height - metrics.BottomInset;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private DockMetrics GetMetrics()
    {
        var dpi = _hwnd == 0 ? 96u : GetDpiForWindow(_hwnd);
        var scale = Math.Clamp(dpi / 96f, 1f, 2.5f);
        var iconSlot = Scale(78, scale);
        var itemGap = Scale(12, scale);
        var sidePadding = Scale(22, scale);
        var sideShadow = Scale(28, scale);
        var itemCount = 1
            + Math.Clamp(_viewModel.PinnedDockItems.Count, 0, 8)
            + Math.Clamp(_viewModel.RunningDockItems.Count, 0, 4);
        var separatorGap = _viewModel.RunningDockItems.Count > 0 ? Scale(18, scale) : 0;
        var contentWidth = (itemCount * iconSlot) + (Math.Max(0, itemCount - 1) * itemGap) + separatorGap;
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
            sideShadow);
    }

    private void RebuildVisualTree()
    {
        if (_compositor is null || _root is null || _width <= 0 || _height <= 0)
        {
            return;
        }

        _hitTargets.Clear();
        _root.Children.RemoveAll();
        _root.Size = new Vector2(_width, _height);
        var metrics = GetMetrics();
        var dockRect = new Windows.Foundation.Rect(metrics.SideShadow, metrics.TopShadow, _width - (metrics.SideShadow * 2), metrics.DockHeight);
        var dockSize = new Vector2((float)dockRect.Width, (float)dockRect.Height);
        var dockOffset = new Vector3((float)dockRect.X, (float)dockRect.Y, 0);

        var shadow = CreateRoundedShape(dockSize + new Vector2(18, 18), metrics.CornerRadius + 8, Windows.UI.Color.FromArgb(60, 0, 0, 0));
        shadow.Offset = dockOffset + new Vector3(-9, 15, 0);
        _root.Children.InsertAtTop(shadow);

        var glass = _compositor.CreateSpriteVisual();
        glass.Size = dockSize;
        glass.Offset = dockOffset;
        glass.Brush = CreateGlassBrush();
        glass.Clip = CreateRoundedClip(dockSize, metrics.CornerRadius);
        _root.Children.InsertAtTop(glass);

        var sheen = CreateGradientVisual(
            dockSize,
            dockOffset,
            [
                (0f, Windows.UI.Color.FromArgb(112, 255, 255, 255)),
                (1f, Windows.UI.Color.FromArgb(10, 255, 255, 255))
            ],
            metrics.CornerRadius);
        _root.Children.InsertAtTop(sheen);

        var lowerShade = CreateGradientVisual(
            dockSize,
            dockOffset,
            [
                (0f, Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                (1f, Windows.UI.Color.FromArgb(38, 0, 0, 0))
            ],
            metrics.CornerRadius);
        _root.Children.InsertAtTop(lowerShade);

        var stroke = CreateRoundedStroke(dockSize, metrics.CornerRadius, Windows.UI.Color.FromArgb(124, 255, 255, 255), Math.Max(1f, metrics.DpiScale));
        stroke.Offset = dockOffset;
        _root.Children.InsertAtTop(stroke);

        var itemCount = 1 + Math.Clamp(_viewModel.PinnedDockItems.Count, 0, 8) + Math.Clamp(_viewModel.RunningDockItems.Count, 0, 4);
        var contentWidth = (itemCount * metrics.IconSlot) + (Math.Max(0, itemCount - 1) * metrics.ItemGap) + (_viewModel.RunningDockItems.Count > 0 ? metrics.SeparatorGap : 0);
        var x = (_width - contentWidth) / 2f;
        var y = (float)dockRect.Y + (((float)dockRect.Height - metrics.IconSlot) / 2f);
        AddHomeIcon(x, y, metrics);
        x += metrics.IconSlot + metrics.ItemGap;

        foreach (var item in _viewModel.PinnedDockItems.Take(8))
        {
            AddDockItem(item, x, y, metrics, showIndicator: item.IsRunning);
            x += metrics.IconSlot + metrics.ItemGap;
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
            AddDockItem(item, x, y, metrics, showIndicator: true);
            x += metrics.IconSlot + metrics.ItemGap;
        }
    }

    private void AddHomeIcon(float x, float y, DockMetrics metrics)
    {
        if (_compositor is null || _root is null)
        {
            return;
        }

        var bounds = Centered(x, y, metrics.IconSlot, metrics.IconSize);
        var icon = CreateRoundedShape(new Vector2(metrics.IconSize), Math.Max(12, metrics.IconSize / 5f), Windows.UI.Color.FromArgb(255, 255, 126, 67));
        icon.Offset = new Vector3((float)bounds.X, (float)bounds.Y, 0);
        _root.Children.InsertAtTop(icon);
        AddGlyphLine((float)bounds.X + (metrics.IconSize * 0.24f), (float)bounds.Y + (metrics.IconSize * 0.52f), (float)bounds.X + (metrics.IconSize * 0.50f), (float)bounds.Y + (metrics.IconSize * 0.26f), metrics);
        AddGlyphLine((float)bounds.X + (metrics.IconSize * 0.50f), (float)bounds.Y + (metrics.IconSize * 0.26f), (float)bounds.X + (metrics.IconSize * 0.76f), (float)bounds.Y + (metrics.IconSize * 0.52f), metrics);
        AddGlyphLine((float)bounds.X + (metrics.IconSize * 0.31f), (float)bounds.Y + (metrics.IconSize * 0.50f), (float)bounds.X + (metrics.IconSize * 0.31f), (float)bounds.Y + (metrics.IconSize * 0.76f), metrics);
        AddGlyphLine((float)bounds.X + (metrics.IconSize * 0.69f), (float)bounds.Y + (metrics.IconSize * 0.50f), (float)bounds.X + (metrics.IconSize * 0.69f), (float)bounds.Y + (metrics.IconSize * 0.76f), metrics);
        AddGlyphLine((float)bounds.X + (metrics.IconSize * 0.31f), (float)bounds.Y + (metrics.IconSize * 0.76f), (float)bounds.X + (metrics.IconSize * 0.69f), (float)bounds.Y + (metrics.IconSize * 0.76f), metrics);
        _hitTargets.Add(new DockHitTarget(new Windows.Foundation.Rect(x, y, metrics.IconSlot, metrics.IconSlot), null, true));
    }

    private void AddDockItem(DockItemViewModel item, float x, float y, DockMetrics metrics, bool showIndicator)
    {
        if (_compositor is null || _root is null)
        {
            return;
        }

        var bounds = Centered(x, y, metrics.IconSlot, metrics.IconSize);
        var iconBrush = GetIconBrush(item.IconPath);
        var icon = _compositor.CreateSpriteVisual();
        icon.Size = new Vector2(metrics.IconSize);
        icon.Offset = new Vector3((float)bounds.X, (float)bounds.Y, 0);
        icon.Brush = iconBrush ?? CreateFallbackIconBrush(item.DisplayName);
        icon.Clip = CreateRoundedClip(new Vector2(metrics.IconSize), Math.Max(12, metrics.IconSize / 5f));
        _root.Children.InsertAtTop(icon);

        if (showIndicator)
        {
            var indicator = CreateRoundedShape(new Vector2(Scale(8, metrics.DpiScale), Scale(3, metrics.DpiScale)), Scale(2, metrics.DpiScale), Windows.UI.Color.FromArgb(255, 10, 132, 255));
            indicator.Offset = new Vector3(x + ((metrics.IconSlot - indicator.Size.X) / 2f), y + metrics.IconSlot - Scale(9, metrics.DpiScale), 0);
            _root.Children.InsertAtTop(indicator);
        }

        _hitTargets.Add(new DockHitTarget(new Windows.Foundation.Rect(x, y, metrics.IconSlot, metrics.IconSlot), item, false));
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
        if (target.IsHome)
        {
            App.ShowMainShell();
            return;
        }

        if (target.Item is not null)
        {
            await _viewModel.OpenDockItemCommand.ExecuteAsync(target.Item);
            _homeMode = false;
        }
    }

    private nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_NCHITTEST:
                return HTCLIENT;
            case WM_LBUTTONUP:
                HandleClientClick(GetPointFromLParam(lParam));
                return 0;
            case WM_POINTERUP:
                if (TryGetPointerPoint(wParam, out var pointerPoint))
                {
                    HandleClientClick(pointerPoint);
                    return 0;
                }

                break;
            case WM_DPICHANGED:
            case WM_DISPLAYCHANGE:
                PositionWindow();
                RebuildVisualTree();
                return 0;
            case WM_DESTROY:
                _target?.Dispose();
                _target = null;
                _root = null;
                _hwnd = 0;
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

        PositionWindow();
        RebuildVisualTree();
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
        var pointerId = (uint)(wParam & 0xFFFF);
        if (GetPointerInfo(pointerId, out var info))
        {
            var nativePoint = info.PixelLocation;
            _ = ScreenToClient(_hwnd, ref nativePoint);
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
        var interop = (ICompositorDesktopInterop)(object)compositor;
        interop.CreateDesktopWindowTarget(hwnd, true, out var target);
        return target;
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
        int SideShadow);

    private sealed record DockHitTarget(Windows.Foundation.Rect Bounds, DockItemViewModel? Item, bool IsHome);

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwndTarget, bool isTopmost, out DesktopWindowTarget target);
    }

    private delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);

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
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_POINTERUP = 0x0247;
    private const uint WM_DPICHANGED = 0x02E0;
    private const int DQTYPE_THREAD_CURRENT = 2;
    private const int DQTAT_COM_STA = 2;
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
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetPointerInfo(uint pointerId, out PointerInfo pointerInfo);

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint dispatcherQueueController);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
