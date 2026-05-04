using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace CoreDesk_Dock;

public sealed partial class DockOverlayWindow : Window
{
    private readonly DispatcherTimer _autoHide = new();
    private readonly DispatcherTimer _foregroundMonitor = new();
    private bool _initialized;
    private bool _isRaised;
    private bool _isGestureActive;
    private double _gestureStartY;
    private Storyboard? _dockStoryboard;

    public ShellViewModel ViewModel { get; } = App.Services.CreateShellViewModel();

    public DockOverlayWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Dock.SetViewModel(ViewModel);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigureWindow();

        _autoHide.Interval = TimeSpan.FromSeconds(3);
        _autoHide.Tick += (_, _) =>
        {
            _autoHide.Stop();
            if (IsForegroundLargeNonDockWindow())
            {
                LowerDock();
            }
        };

        _foregroundMonitor.Interval = TimeSpan.FromMilliseconds(500);
        _foregroundMonitor.Tick += (_, _) => UpdateForForegroundWindow();
        _foregroundMonitor.Start();

        Closed += (_, _) =>
        {
            _autoHide.Stop();
            _foregroundMonitor.Stop();
        };
    }

    public async void ShowDock()
    {
        await EnsureInitializedAsync();
        PositionWindow();
        AppWindow.Show(true);
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        RaiseDock();
    }

    public void RaiseDock()
    {
        _isRaised = true;
        KeepTopMost();
        AnimateDock(-2, 1, 1);
        if (IsForegroundLargeNonDockWindow())
        {
            RestartAutoHide();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync();
        ViewModel.UpdateViewport(GetSystemMetrics(0), GetSystemMetrics(1));
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
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
        EnableWindowAcrylic(handle);
        SetDwmAttributes(handle);
        PositionWindow();
        AppWindow.Hide();
    }

    private void PositionWindow()
    {
        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
        ViewModel.UpdateViewport(screenWidth, screenHeight);

        var dockItemCount = Math.Clamp(ViewModel.PinnedDockItems.Count, 1, 10)
            + Math.Clamp(ViewModel.RunningDockItems.Count, 0, 7)
            + 1;
        var buttonSize = (int)Math.Round(ViewModel.DockButtonSize);
        var requestedWidth = 42 + (dockItemCount * buttonSize) + Math.Max(0, dockItemCount - 1) * 10;
        var width = Math.Clamp(requestedWidth, 560, Math.Min(1320, screenWidth - 220));
        var height = Math.Clamp(buttonSize + 42, 126, 146);
        AppWindow.MoveAndResize(new RectInt32((screenWidth - width) / 2, screenHeight - height - 30, width, height));
        ApplyRoundedWindowRegion(width, height);
        KeepTopMost();
    }

    private void LowerDock()
    {
        if (!_isRaised)
        {
            return;
        }

        _isRaised = false;
        AnimateDock(112, 0.7, 0.985);
    }

    private void AnimateDock(double translationY, double opacity, double scale)
    {
        _dockStoryboard?.Stop();
        Dock.Opacity = opacity;
        DockTransform.TranslateY = translationY;
        DockTransform.ScaleX = scale;
        DockTransform.ScaleY = scale;

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
        _autoHide.Stop();
        _autoHide.Start();
    }

    private void UpdateForForegroundWindow()
    {
        try
        {
            if (!AppWindow.IsVisible)
            {
                return;
            }

            KeepTopMost();
            if (IsForegroundLargeNonDockWindow())
            {
                RestartAutoHide();
                return;
            }

            RaiseDock();
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Error(exception, "Standalone dock foreground update failed.");
        }
    }

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        RaiseDock();
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (IsForegroundLargeNonDockWindow())
        {
            RestartAutoHide();
        }
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
            RaiseDock();
            _isGestureActive = false;
            ReleasePointerCapture(e.Pointer);
        }
    }

    private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isGestureActive = false;
        ReleasePointerCapture(e.Pointer);
        if (IsForegroundLargeNonDockWindow())
        {
            RestartAutoHide();
        }
    }

    private void OnRootPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isGestureActive = false;
        ReleasePointerCapture(e.Pointer);
    }

    private void ReleasePointerCapture(Pointer pointer)
    {
        try
        {
            Root.ReleasePointerCapture(pointer);
        }
        catch
        {
        }
    }

    private void KeepTopMost()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        BringWindowToTop(handle);
        SetForegroundWindow(handle);
    }

    private static bool IsForegroundLargeNonDockWindow()
    {
        var foreground = GetForegroundWindow();
        var dockHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
        if (foreground == 0 || foreground == dockHandle)
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

    private static void SetDwmAttributes(nint handle)
    {
        var cornerPreference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        var borderColor = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
    }

    private static void EnableWindowAcrylic(nint handle)
    {
        var accent = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0x20,
            GradientColor = unchecked((int)0xA8FFFFFF)
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

    private static readonly nint HWND_TOPMOST = new(-1);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int DWMWCP_ROUND = 2;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

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

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

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
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
