using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace CoreDesk_App;

public sealed partial class MainWindow : Window
{
    private MainPage? _mainPage;
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        UseFullScreenShell();
        Content.KeyDown += OnKeyDown;
        RootFrame.Navigated += (_, _) => _mainPage = RootFrame.Content as MainPage;
        RootFrame.Navigate(typeof(MainPage));
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
        BringShellForward();
        _mainPage?.OpenSettings();
    }

    public void OpenDrawer()
    {
        BringShellForward();
        _mainPage?.OpenDrawer();
    }

    public void OpenControlCenter()
    {
        App.ShowControlCenterOverlay();
    }

    public void OpenTaskSwitcher()
    {
        BringShellForward();
        _mainPage?.OpenTaskSwitcher();
    }

    public void ShowHome()
    {
        _mainPage?.ShowHome();
    }

    public void UseFullScreenShell()
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
        AppWindow.MoveAndResize(new RectInt32(0, 0, GetSystemMetrics(0), GetSystemMetrics(1)));
        HideDwmChrome(handle);
    }

    public void ConfigureAsDesktopLayer()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        SendToDesktopLayer();
    }

    public void SendToDesktopLayer()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowPos(handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void BringShellForward()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, style & ~WS_EX_NOACTIVATE);
        SetWindowPos(handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        Activate();
    }

    private static void HideDwmChrome(nint handle)
    {
        var cornerPreference = DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        var borderColor = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
    }

    private static void SetPopupWindowStyle(nint handle)
    {
        var style = GetWindowLongPtr(handle, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        style |= WS_POPUP;
        SetWindowLongPtr(handle, GWL_STYLE, style);
    }

    private static bool IsControlAltPressed()
    {
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        var menu = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
        return control.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            && menu.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static readonly nint HWND_BOTTOM = new(1);
    private static readonly nint HWND_TOP = new(0);
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newLong);
}
