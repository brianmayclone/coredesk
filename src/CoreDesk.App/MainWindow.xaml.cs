using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;

namespace CoreDesk_App;

public sealed partial class MainWindow : Window
{
    private MainPage? _mainPage;
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        Content.KeyDown += OnKeyDown;
        RootFrame.Navigated += (_, _) => _mainPage = RootFrame.Content as MainPage;
        RootFrame.Navigate(typeof(MainPage));
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
        _mainPage?.OpenSettings();
    }

    public void OpenDrawer()
    {
        _mainPage?.OpenDrawer();
    }

    public void OpenControlCenter()
    {
        _mainPage?.OpenControlCenter();
    }

    public void OpenTaskSwitcher()
    {
        _mainPage?.OpenTaskSwitcher();
    }

    public void UseFullScreenShell()
    {
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        KeepTopMost();
    }

    private static void KeepTopMost()
    {
        SetWindowPos(App.WindowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static bool IsControlAltPressed()
    {
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        var menu = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
        return control.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            && menu.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private static readonly nint HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

}
