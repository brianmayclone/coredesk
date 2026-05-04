using CoreDesk.Abstractions.Models;
using Microsoft.UI.Xaml;

namespace CoreDesk_Dock;

public partial class App : Application
{
    public static DockComposition Services { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static DockOverlayWindow Window { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            Services?.Diagnostics.Error(args.Exception, "Unhandled dock exception.");
            args.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var launchArguments = GetLaunchArguments(args.Arguments);
        Services = new DockComposition(LaunchOptions.Parse(launchArguments));
        Services.Diagnostics.Info($"CoreDesk.Dock launched with args: {launchArguments}");
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window = new DockOverlayWindow();
        Window.Closed += (_, _) => Services.Dispose();
        Window.ShowDock();
    }

    private static string GetLaunchArguments(string winUiArguments)
    {
        return string.IsNullOrWhiteSpace(winUiArguments)
            ? string.Join(" ", Environment.GetCommandLineArgs().Skip(1))
            : winUiArguments;
    }
}
