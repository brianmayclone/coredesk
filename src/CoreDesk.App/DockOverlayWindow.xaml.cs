using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace CoreDesk_App;

public sealed partial class DockOverlayWindow : Window
{
    private readonly DispatcherTimer _autoHide = new();
    private readonly DispatcherTimer _foregroundMonitor = new();
    private bool _initialized;
    private bool _homeMode = true;
    private bool _isRaised;
    private bool _isGestureActive;
    private double _gestureStartY;
    private Storyboard? _dockStoryboard;

    public ShellViewModel ViewModel { get; }

    public DockOverlayWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Root.DataContext = ViewModel;
        Dock.SetViewModel(ViewModel);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigureWindow();
        ViewModel.PinnedDockItems.CollectionChanged += OnDockItemsChanged;
        ViewModel.RunningDockItems.CollectionChanged += OnDockItemsChanged;

        _autoHide.Interval = TimeSpan.FromSeconds(5);
        _autoHide.Tick += (_, _) =>
        {
            _autoHide.Stop();
            LowerDock();
        };

        _foregroundMonitor.Interval = TimeSpan.FromMilliseconds(700);
        _foregroundMonitor.Tick += (_, _) => UpdateDockVisibilityForForegroundWindow();
        _foregroundMonitor.Start();
        Closed += (_, _) =>
        {
            _autoHide.Stop();
            _foregroundMonitor.Stop();
            ViewModel.PinnedDockItems.CollectionChanged -= OnDockItemsChanged;
            ViewModel.RunningDockItems.CollectionChanged -= OnDockItemsChanged;
        };
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await App.EnsureShellReadyAsync();
        _initialized = true;
        ViewModel.UpdateViewport(GetSystemMetrics(0), GetSystemMetrics(1));
    }

    public async void ShowDock(bool homeMode = false)
    {
        try
        {
            _homeMode = homeMode;
            await EnsureInitializedAsync();
            PositionWindow();
            AppWindow.Show(false);
            KeepTopMost();
            RaiseDock();
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, "Dock overlay failed to show.");
        }
    }

    public void HideDock()
    {
        _autoHide.Stop();
        AppWindow.Hide();
    }

    private void ConfigureWindow()
    {
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetPopupWindowStyle(handle);
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE);
        EnableDwmBlur(handle);
        EnableWindowAcrylic(handle);
        SetRoundedWindowCorners(handle);
        HideDwmBorder(handle);
        KeepTopMost();
        PositionWindow();
        AppWindow.Hide();
    }

    private void PositionWindow()
    {
        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
        ViewModel.UpdateViewport(screenWidth, screenHeight);
        var metrics = GetNativeDockMetrics();
        var width = Math.Min(metrics.WindowWidth, screenWidth - (metrics.ScreenInset * 2));
        var height = metrics.WindowHeight;
        AppWindow.MoveAndResize(new RectInt32((screenWidth - width) / 2, screenHeight - height - 22, width, height));
        ApplyRoundedWindowRegion(width, height);
    }

    private DockMetrics GetNativeDockMetrics()
    {
        var scale = Math.Clamp(GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96f, 1f, 2.5f);
        var iconSlot = Scale(78, scale);
        var itemGap = Scale(12, scale);
        var sidePadding = Scale(22, scale);
        var sideShadow = Scale(28, scale);
        var itemCount = Math.Max(
            1,
            1
            + Math.Clamp(ViewModel.PinnedDockItems.Count, 0, 8)
            + Math.Clamp(ViewModel.RunningDockItems.Count, 0, 4));
        var separatorWidth = ViewModel.RunningDockItems.Count > 0 ? Scale(18, scale) : 0;
        var contentWidth = (itemCount * iconSlot) + (Math.Max(0, itemCount - 1) * itemGap) + separatorWidth;
        var dockWidth = contentWidth + (sidePadding * 2);
        return new DockMetrics(
            WindowWidth: dockWidth + (sideShadow * 2),
            WindowHeight: Math.Max(Scale(132, scale), iconSlot + Scale(54, scale)),
            ScreenInset: Scale(34, scale));
    }

    private static int Scale(int value, float scale) => Math.Max(1, (int)Math.Round(value * scale));

    private void OnDockItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!AppWindow.IsVisible)
        {
            return;
        }

        PositionWindow();
        KeepTopMost();
    }

    private void RaiseDock()
    {
        _isRaised = true;
        KeepTopMost();
        AnimateDock(-2, 1, 1);
        if (!_homeMode)
        {
            RestartAutoHide();
        }
    }

    private void LowerDock()
    {
        if (!_isRaised)
        {
            return;
        }

        _isRaised = false;
        AnimateDock(102, 0.78, 0.985);
    }

    private void AnimateDock(double translationY, double opacity, double scale)
    {
        _dockStoryboard?.Stop();
        Dock.Opacity = opacity;

        var easing = new CircleEase { EasingMode = translationY <= 0 ? EasingMode.EaseOut : EasingMode.EaseIn };
        _dockStoryboard = new Storyboard();
        AddDoubleAnimation(_dockStoryboard, DockTransform, "TranslateY", translationY, 320, easing);
        AddDoubleAnimation(_dockStoryboard, DockTransform, "ScaleX", scale, 320, easing);
        AddDoubleAnimation(_dockStoryboard, DockTransform, "ScaleY", scale, 320, easing);
        _dockStoryboard.Begin();
    }

    private static void AddDoubleAnimation(Storyboard storyboard, DependencyObject target, string propertyPath, double to, int milliseconds, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        storyboard.Children.Add(animation);
    }

    private void RestartAutoHide()
    {
        if (_homeMode)
        {
            _autoHide.Stop();
            return;
        }

        _autoHide.Stop();
        _autoHide.Start();
    }

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        RaiseDock();
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        RestartAutoHide();
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isGestureActive = true;
        _gestureStartY = e.GetCurrentPoint(Root).Position.Y;
        Root.CapturePointer(e.Pointer);
        RaiseDock();
    }

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isGestureActive)
        {
            return;
        }

        var currentY = e.GetCurrentPoint(Root).Position.Y;
        if (_gestureStartY - currentY > 70)
        {
            App.ShowMainShell(openTaskSwitcher: true);
            _isGestureActive = false;
            ReleasePointerCapture(e.Pointer);
        }
    }

    private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isGestureActive)
        {
            var currentY = e.GetCurrentPoint(Root).Position.Y;
            if (_gestureStartY - currentY > 70)
            {
                App.ShowMainShell(openTaskSwitcher: true);
            }
        }

        _isGestureActive = false;
        ReleasePointerCapture(e.Pointer);
        RestartAutoHide();
    }

    private void OnRootPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isGestureActive = false;
        ReleasePointerCapture(e.Pointer);
        RestartAutoHide();
    }

    private void ReleasePointerCapture(Pointer pointer)
    {
        try
        {
            Root.ReleasePointerCapture(pointer);
        }
        catch
        {
            // WinUI can deliver cancellation after capture has already been released.
        }
    }

    private void UpdateDockVisibilityForForegroundWindow()
    {
        try
        {
            if (!AppWindow.IsVisible)
            {
                return;
            }

            if (IsForegroundLargeNonCoreDeskWindow())
            {
                _homeMode = false;
                RestartAutoHide();
                return;
            }

            if (_homeMode)
            {
                return;
            }

            RaiseDock();
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, "Dock foreground monitor failed.");
            return;
        }
    }

    private void KeepTopMost()
    {
        SetWindowPos(WinRT.Interop.WindowNative.GetWindowHandle(this), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static void SetRoundedWindowCorners(nint handle)
    {
        var preference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    private static void HideDwmBorder(nint handle)
    {
        var borderColor = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
    }

    private static void EnableDwmBlur(nint handle)
    {
        var blur = new DwmBlurBehind
        {
            Flags = DWM_BB_ENABLE,
            Enable = true
        };
        _ = DwmEnableBlurBehindWindow(handle, ref blur);
    }

    private static void EnableWindowAcrylic(nint handle)
    {
        var accent = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0x20,
            GradientColor = unchecked((int)0x78FFFFFF)
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
            _ = SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    private static void SetPopupWindowStyle(nint handle)
    {
        var style = GetWindowLongPtr(handle, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        style |= WS_POPUP;
        SetWindowLongPtr(handle, GWL_STYLE, style);
    }

    private static bool IsForegroundLargeNonCoreDeskWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == App.WindowHandle)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        if (!GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        return width >= screenWidth * 0.72 && height >= screenHeight * 0.72;
    }

    private void ApplyRoundedWindowRegion(int width, int height)
    {
        var cornerDiameter = Math.Min(height, 88);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, cornerDiameter, cornerDiameter);
        if (region == 0)
        {
            return;
        }

        if (SetWindowRgn(WinRT.Interop.WindowNative.GetWindowHandle(this), region, true) == 0)
        {
            _ = DeleteObject(region);
        }
    }

    private static readonly nint HWND_TOPMOST = new(-1);
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int DWMWCP_ROUND = 2;
    private const int DWM_BB_ENABLE = 0x00000001;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref DwmBlurBehind blurBehind);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

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
        public nint Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public int Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Enable;
        public nint RegionBlur;
        [MarshalAs(UnmanagedType.Bool)]
        public bool TransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record DockMetrics(int WindowWidth, int WindowHeight, int ScreenInset);
}
