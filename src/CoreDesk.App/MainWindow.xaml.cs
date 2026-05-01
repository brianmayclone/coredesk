using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

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
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            App.Services.SystemIntegration.SetTaskbarVisible(true);
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

    private static bool IsControlAltPressed()
    {
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        var menu = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
        return control.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            && menu.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }
}
