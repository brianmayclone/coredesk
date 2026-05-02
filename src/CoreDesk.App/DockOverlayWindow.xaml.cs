using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace CoreDesk_App;

public sealed partial class DockOverlayWindow : Window
{
    private readonly DispatcherTimer _autoHide = new();
    private bool _initialized;
    private bool _isRaised;
    private bool _isGestureActive;
    private double _gestureStartY;
    private Storyboard? _dockStoryboard;

    public ShellViewModel ViewModel { get; } = App.Services.CreateShellViewModel();

    public DockOverlayWindow()
    {
        InitializeComponent();
        Root.DataContext = ViewModel;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigureWindow();

        _autoHide.Interval = TimeSpan.FromSeconds(3);
        _autoHide.Tick += (_, _) =>
        {
            _autoHide.Stop();
            LowerDock();
        };
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync();
        ViewModel.UpdateViewport(GetSystemMetrics(0), GetSystemMetrics(1));
        Bindings.Update();
    }

    public async void ShowDock()
    {
        await EnsureInitializedAsync();
        PositionWindow();
        AppWindow.Show(true);
        RaiseDock();
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
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
        SetRoundedWindowCorners(handle);
        KeepTopMost();
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
            + 4;
        var buttonSize = (int)Math.Round(ViewModel.DockButtonSize);
        var requestedWidth = 40 + (dockItemCount * buttonSize) + Math.Max(0, dockItemCount - 1) * 12 + 18;
        var width = Math.Clamp(requestedWidth, 560, Math.Min(1420, screenWidth - 180));
        var height = Math.Clamp(buttonSize + 34, 116, 132);
        AppWindow.MoveAndResize(new RectInt32((screenWidth - width) / 2, screenHeight - height - 26, width, height));
        ApplyRoundedWindowRegion(width, height);
    }

    private void RaiseDock()
    {
        _isRaised = true;
        KeepTopMost();
        AnimateDock(-2, 1, 1);
        RestartAutoHide();
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

        var easing = new CircleEase { EasingMode = translationY <= 0 ? EasingMode.EaseOut : EasingMode.EaseIn };
        _dockStoryboard = new Storyboard();
        AddDoubleAnimation(_dockStoryboard, DockTransform, "TranslateY", translationY, 320, easing);
        AddDoubleAnimation(_dockStoryboard, DockTransform, "ScaleX", scale, 320, easing);
        AddDoubleAnimation(_dockStoryboard, DockTransform, "ScaleY", scale, 320, easing);
        AddDoubleAnimation(_dockStoryboard, LiquidDock, "Opacity", opacity, 210, easing);
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

    private async void OnDockItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DockItemViewModel item })
        {
            await ViewModel.OpenDockItemCommand.ExecuteAsync(item);
            RestartAutoHide();
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        App.ShowMainShell(openSettings: true);
    }

    private void OnDrawerClick(object sender, RoutedEventArgs e)
    {
        App.ShowMainShell(openDrawer: true);
    }

    private void OnControlCenterClick(object sender, RoutedEventArgs e)
    {
        App.ShowMainShell(openControlCenter: true);
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
            Root.ReleasePointerCapture(e.Pointer);
        }
    }

    private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isGestureActive = false;
        Root.ReleasePointerCapture(e.Pointer);
        RestartAutoHide();
    }

    private void OnRootPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isGestureActive = false;
        Root.ReleasePointerCapture(e.Pointer);
        RestartAutoHide();
    }

    private void OnDockButtonPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.CenterPoint = new System.Numerics.Vector3((float)(button.ActualWidth / 2), (float)(button.ActualHeight / 2), 0);
            button.Scale = new System.Numerics.Vector3(1.14f, 1.14f, 1);
            button.Translation = new System.Numerics.Vector3(0, -7, 0);
        }
    }

    private void OnDockButtonPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Scale = System.Numerics.Vector3.One;
            button.Translation = System.Numerics.Vector3.Zero;
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

    private void ApplyRoundedWindowRegion(int width, int height)
    {
        const int cornerDiameter = 84;
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
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);
}
