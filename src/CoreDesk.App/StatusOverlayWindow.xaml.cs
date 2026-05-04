using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace CoreDesk_App;

public sealed partial class StatusOverlayWindow : Window
{
    public const int ReservedHeight = 42;

    private readonly DispatcherTimer _clock = new();
    private readonly DispatcherTimer _foregroundMonitor = new();
    private Storyboard? _statusStoryboard;
    private bool _initialized;
    private bool _homeMode = true;
    private double _targetBackdropOpacity = -1;
    private Windows.Foundation.Point? _pointerStart;

    public ShellViewModel ViewModel { get; }

    public nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public StatusOverlayWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Root.DataContext = ViewModel;
        Root.Tag = new SolidColorBrush(Microsoft.UI.Colors.White);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigureWindow();

        _clock.Interval = TimeSpan.FromSeconds(15);
        _clock.Tick += (_, _) => ViewModel.Tick();
        _clock.Start();

        _foregroundMonitor.Interval = TimeSpan.FromMilliseconds(700);
        _foregroundMonitor.Tick += (_, _) => ApplyForegroundStyle();
        _foregroundMonitor.Start();
        Closed += (_, _) =>
        {
            _clock.Stop();
            _foregroundMonitor.Stop();
        };
    }

    public async void ShowStatus(bool homeMode)
    {
        try
        {
            _homeMode = homeMode;
            if (!_initialized)
            {
                await App.EnsureShellReadyAsync();
                _initialized = true;
            }

            PositionWindow();
            ApplyForegroundStyle();
            AppWindow.Show(false);
            KeepTopMost();
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, "Status overlay failed to show.");
        }
    }

    public void HideStatus()
    {
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
        HideDwmBorder(handle);
        PositionWindow();
        AppWindow.Hide();
    }

    private void PositionWindow()
    {
        AppWindow.MoveAndResize(new RectInt32(0, 0, GetSystemMetrics(0), ReservedHeight));
    }

    private void ApplyForegroundStyle()
    {
        var hasLargeForegroundApp = IsForegroundLargeNonCoreDeskWindow();
        var isHomescreenOnly = _homeMode && !hasLargeForegroundApp;
        Root.Tag = new SolidColorBrush(Microsoft.UI.Colors.White);
        AnimateStatusBackdrop(isHomescreenOnly ? 0 : 1);
    }

    private void AnimateStatusBackdrop(double opacity)
    {
        if (Math.Abs(_targetBackdropOpacity - opacity) < 0.001)
        {
            return;
        }

        _targetBackdropOpacity = opacity;
        _statusStoryboard?.Stop();
        var animation = new DoubleAnimation
        {
            To = opacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(240)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, DarkStatusBackdrop);
        Storyboard.SetTargetProperty(animation, "Opacity");
        _statusStoryboard = new Storyboard();
        _statusStoryboard.Children.Add(animation);
        _statusStoryboard.Begin();
    }

    private void KeepTopMost()
    {
        SetWindowPos(WinRT.Interop.WindowNative.GetWindowHandle(this), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pointerStart = e.GetCurrentPoint(Root).Position;
    }

    private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerStart is null)
        {
            return;
        }

        var start = _pointerStart.Value;
        var end = e.GetCurrentPoint(Root).Position;
        _pointerStart = null;
        var deltaY = end.Y - start.Y;
        var deltaX = end.X - start.X;
        if (start.X < Root.ActualWidth * 0.5 || deltaY < 24 || Math.Abs(deltaX) > deltaY)
        {
            return;
        }

        OpenControlCenter();
        e.Handled = true;
    }

    private void OnControlCenterHotZoneTapped(object sender, TappedRoutedEventArgs e)
    {
        OpenControlCenter();
        e.Handled = true;
    }

    private static void OpenControlCenter()
    {
        App.ShowMainShell(openControlCenter: true);
    }

    private static void SetPopupWindowStyle(nint handle)
    {
        var style = GetWindowLongPtr(handle, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        style |= WS_POPUP;
        SetWindowLongPtr(handle, GWL_STYLE, style);
    }

    private static void HideDwmBorder(nint handle)
    {
        var cornerPreference = DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        var borderColor = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
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
        return rect.Right - rect.Left >= screenWidth * 0.72
            && rect.Bottom - rect.Top >= screenHeight * 0.72;
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
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
