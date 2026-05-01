using CoreDesk.Abstractions.Models;
using Microsoft.UI.Xaml;

namespace CoreDesk_App;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public static AppComposition Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try
            {
                Services?.SystemIntegration.SetTaskbarVisible(true);
                Services?.Diagnostics.Error(args.Exception, "Unhandled exception.");
            }
            catch
            {
                WriteFallbackCrashLog(args.Exception);
            }
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            var launchArguments = GetLaunchArguments(args.Arguments);
            Services = new AppComposition(LaunchOptions.Parse(launchArguments));
            Services.Diagnostics.Info($"CoreDesk launched with args: {launchArguments}");
            if (Services.Options.ResetConfig)
            {
                Services.ConfigurationStore.ResetAsync().GetAwaiter().GetResult();
            }

            Services.SystemIntegration.Initialize();
            Services.SystemIntegration.CommandRequested += OnSystemCommandRequested;
            Window = new MainWindow();
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            WireHardwareModeSwitching();
            Window.Activate();
            if (!Services.Options.SafeMode)
            {
                Services.SystemIntegration.SetTaskbarVisible(false);
            }
        }
        catch (Exception exception)
        {
            Services?.SystemIntegration.SetTaskbarVisible(true);
            Services?.Diagnostics.Error(exception, "Launch failed.");
            WriteFallbackCrashLog(exception);
            throw;
        }
    }

    private static string GetLaunchArguments(string winUiArguments)
    {
        if (!string.IsNullOrWhiteSpace(winUiArguments))
        {
            return winUiArguments;
        }

        return string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
    }

    private static void WireHardwareModeSwitching()
    {
        if (Services.Options.SafeMode)
        {
            Services.Diagnostics.Info("Safe mode enabled; hardware auto-switch disabled.");
            return;
        }

        Services.HardwareMonitor.HardwareStateChanged += (_, args) =>
        {
            if (args.Snapshot.IsKeyboardPresent)
            {
                DispatcherQueue.TryEnqueue(Services.ShellMode.EnterDesktopMode);
            }
            else
            {
                DispatcherQueue.TryEnqueue(Services.ShellMode.EnterTouchMode);
            }
        };

        Services.HardwareMonitor.Start();
    }

    private static void OnSystemCommandRequested(object? sender, SystemIntegrationCommand command)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (command)
            {
                case SystemIntegrationCommand.OpenShell:
                    Window.Activate();
                    Services.ShellMode.EnterTouchMode();
                    break;
                case SystemIntegrationCommand.EnterTouchMode:
                    Window.Activate();
                    Services.ShellMode.EnterTouchMode();
                    break;
                case SystemIntegrationCommand.EnterDesktopMode:
                    Services.ShellMode.EnterDesktopMode();
                    break;
                case SystemIntegrationCommand.OpenSettings:
                    Window.Activate();
                    if (Window is MainWindow mainWindow)
                    {
                        mainWindow.OpenSettings();
                    }
                    break;
                case SystemIntegrationCommand.EnterSafeMode:
                    Services.SystemIntegration.SetTaskbarVisible(true);
                    Window.Activate();
                    break;
                case SystemIntegrationCommand.Exit:
                    Services.SystemIntegration.SetTaskbarVisible(true);
                    Window.Close();
                    break;
            }
        });
    }

    private static void WriteFallbackCrashLog(Exception exception)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreDesk", "logs");
        Directory.CreateDirectory(directory);
        File.AppendAllText(
            Path.Combine(directory, "fallback-crash.log"),
            $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}");
    }
}
