using CoreDesk.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace CoreDesk_App;

public sealed partial class ControlCenterOverlayWindow : Window
{
    private Storyboard? _storyboard;
    private bool _isShown;

    public ShellViewModel ViewModel { get; }

    public ControlCenterOverlayWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Root.DataContext = ViewModel;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigureWindow();
    }

    public void ShowOverlay()
    {
        ViewModel.Tick();
        PositionWindow();
        AppWindow.Show(false);
        KeepTopMost();
        _isShown = true;
        AnimatePanel(show: true);
    }

    public void HideOverlay()
    {
        if (!_isShown)
        {
            AppWindow.Hide();
            return;
        }

        _isShown = false;
        AnimatePanel(show: false);
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
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
        var exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
        HideDwmBorder(handle);
        PositionWindow();
        AppWindow.Hide();
    }

    private void PositionWindow()
    {
        AppWindow.MoveAndResize(new RectInt32(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)));
    }

    private void KeepTopMost()
    {
        SetWindowPos(WinRT.Interop.WindowNative.GetWindowHandle(this), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void AnimatePanel(bool show)
    {
        _storyboard?.Stop();
        var duration = new Duration(TimeSpan.FromMilliseconds(show ? 260 : 180));
        var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var opacity = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = duration,
            EasingFunction = easing
        };
        var translateY = new DoubleAnimation
        {
            To = show ? 0 : -18,
            Duration = duration,
            EasingFunction = easing
        };
        var scale = new DoubleAnimation
        {
            To = show ? 1 : 0.985,
            Duration = duration,
            EasingFunction = easing
        };

        Storyboard.SetTarget(opacity, ControlCenterPanel);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        Storyboard.SetTarget(translateY, ControlCenterPanel);
        Storyboard.SetTargetProperty(translateY, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");
        Storyboard.SetTarget(scale, ControlCenterPanel);
        Storyboard.SetTargetProperty(scale, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)");

        var scaleY = new DoubleAnimation
        {
            To = show ? 1 : 0.985,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(scaleY, ControlCenterPanel);
        Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)");

        _storyboard = new Storyboard();
        _storyboard.Children.Add(opacity);
        _storyboard.Children.Add(translateY);
        _storyboard.Children.Add(scale);
        _storyboard.Children.Add(scaleY);
        if (!show)
        {
            _storyboard.Completed += (_, _) => AppWindow.Hide();
        }

        _storyboard.Begin();
    }

    private void OnBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        HideOverlay();
        e.Handled = true;
    }

    private void OnPanelTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideOverlay();
            e.Handled = true;
        }
    }

    private void OnVolumeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_isShown)
        {
            return;
        }

        ViewModel.SetVolumePercent((int)Math.Round(e.NewValue));
        Bindings.Update();
    }

    private void OnBrightnessSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_isShown)
        {
            return;
        }

        ViewModel.SetBrightnessPercent((int)Math.Round(e.NewValue));
        Bindings.Update();
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
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
