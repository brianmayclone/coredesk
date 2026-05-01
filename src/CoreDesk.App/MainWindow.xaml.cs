using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace CoreDesk_App;

public sealed partial class MainWindow : Window
{
    private MainPage? _mainPage;
    private bool _isDesktopOverlay;

    public MainWindow()
    {
        InitializeComponent();

        EnableTransparentColorKey(WinRT.Interop.WindowNative.GetWindowHandle(this));
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        Content.KeyDown += OnKeyDown;
        RootFrame.Navigated += (_, _) => _mainPage = RootFrame.Content as MainPage;
        RootFrame.Navigate(typeof(MainPage));
        App.Services.ShellMode.ModeChanged += (_, mode) =>
        {
            if (mode == CoreDesk.Abstractions.Models.ShellMode.Desktop)
            {
                UseDesktopDockOverlay();
            }
            else
            {
                UseFullScreenShell();
            }
        };
        Activated += (_, _) => KeepTopMost();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            App.Services.SystemIntegration.SetTaskbarVisible(true);
            App.Services.SystemIntegration.Dispose();
            Close();
        }

        if (e.Key == Windows.System.VirtualKey.T && IsControlAltPressed())
        {
            App.Services.ShellMode.EnterDesktopMode();
        }
    }

    public void OpenSettings()
    {
        UseFullScreenShell();
        _mainPage?.OpenSettings();
    }

    public void ExpandOverlay()
    {
        if (_isDesktopOverlay)
        {
            AppWindow.MoveAndResize(new RectInt32(0, 0, GetSystemMetrics(0), GetSystemMetrics(1)));
            KeepTopMost();
        }
    }

    public void CollapseOverlayToDock()
    {
        if (_isDesktopOverlay)
        {
            var screenWidth = GetSystemMetrics(0);
            var screenHeight = GetSystemMetrics(1);
            var height = Math.Min(156, Math.Max(128, screenHeight / 14));
            var width = Math.Min(1480, Math.Max(1080, screenWidth / 3));
            AppWindow.MoveAndResize(new RectInt32((screenWidth - width) / 2, screenHeight - height - 12, width, height));
            KeepTopMost();
        }
    }

    private void UseDesktopDockOverlay()
    {
        _isDesktopOverlay = true;
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        CollapseOverlayToDock();
    }

    private void UseFullScreenShell()
    {
        _isDesktopOverlay = false;
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        KeepTopMost();
    }

    private static void KeepTopMost()
    {
        SetWindowPos(App.WindowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static void EnableTransparentColorKey(nint handle)
    {
        var extendedStyle = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED);
        SetLayeredWindowAttributes(handle, TransparentColorKey, 255, LWA_COLORKEY);
    }

    private static bool IsControlAltPressed()
    {
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        var menu = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
        return control.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            && menu.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private static readonly nint HWND_TOPMOST = new(-1);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint LWA_COLORKEY = 0x00000001;
    private const uint TransparentColorKey = 0x00000000;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
