using CoreDesk.Abstractions.Models;
using Microsoft.UI.Xaml;

namespace CoreDesk_App;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static NativeDockHost? DockWindow { get; private set; }

    public static StatusOverlayWindow? StatusWindow { get; private set; }

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public static AppComposition Services { get; private set; } = null!;

    private static DispatcherTimer? _desktopLayerEnforcer;
    private static DispatcherTimer? _workAreaEnforcer;
    private static WindowsKeyHook? _windowsKeyHook;
    private static bool _homeExperienceReady;

    public App()
    {
        InitializeComponent();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreSystemShell();
        UnhandledException += (_, args) =>
        {
            try
            {
                Services?.Diagnostics.Error(args.Exception, "Unhandled exception.");
                args.Handled = true;
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
            ApplyShellTaskbarPolicy();
            Services.SystemIntegration.CommandRequested += OnSystemCommandRequested;
            Window = new MainWindow();
            Window.Closed += (_, _) => RestoreSystemShell();
            DockWindow = new NativeDockHost();
            StatusWindow = new StatusOverlayWindow();
            Services.ShellMode.ModeChanged += OnShellModeChanged;
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _windowsKeyHook = new WindowsKeyHook();
            _windowsKeyHook.Install();
            WireHardwareModeSwitching();
            if (Services.Options.ReplaceExplorerForSession)
            {
                Services.Diagnostics.Info("Session shell replacement requested; Explorer shell will be stopped for this run.");
                Services.ShellReplacement.ReplaceExplorerForSession();
            }

            Window.Activate();
            ShowStatusAndReserveWorkArea(homeMode: true);
            if (Window is MainWindow desktopWindow)
            {
                desktopWindow.ConfigureAsDesktopLayer();
                KeepHomescreenBehindWindows();
            }

            SignalShellReadyIfNeeded();
            ApplyShellTaskbarPolicy();
        }
        catch (Exception exception)
        {
            Services?.SystemIntegration.SetTaskbarVisible(true);
            Services?.SystemIntegration.RestoreWorkArea();
            Services?.ShellReplacement.RestoreExplorerForSession();
            Services?.Diagnostics.Error(exception, "Launch failed.");
            WriteFallbackCrashLog(exception);
            throw;
        }
    }

    public static void NotifyHomeExperienceReady(bool homeMode)
    {
        if (_homeExperienceReady)
        {
            ShowDockWhenReady(homeMode);
            return;
        }

        _homeExperienceReady = true;
        Services.Diagnostics.Info("Home experience finished loading; starting native dock.");
        ShowDockWhenReady(homeMode);
    }

    public static void ShowDockWhenReady(bool homeMode)
    {
        if (!_homeExperienceReady)
        {
            return;
        }

        DockWindow?.ShowDock(homeMode);
    }

    public static void ShowStatusAndReserveWorkArea(bool homeMode)
    {
        if (StatusWindow is null)
        {
            return;
        }

        StatusWindow.ShowStatus(homeMode);
        ReserveTopWorkAreaWithRetry();
    }

    private static void ReserveTopWorkAreaWithRetry()
    {
        if (StatusWindow is null)
        {
            return;
        }

        Services.SystemIntegration.ReserveTopWorkArea(StatusWindow.WindowHandle, StatusOverlayWindow.ReservedHeight);

        var remainingTicks = 12;
        _workAreaEnforcer?.Stop();
        _workAreaEnforcer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _workAreaEnforcer.Tick += (_, _) =>
        {
            if (StatusWindow is not null)
            {
                Services.SystemIntegration.ReserveTopWorkArea(StatusWindow.WindowHandle, StatusOverlayWindow.ReservedHeight);
            }

            remainingTicks--;
            if (remainingTicks <= 0)
            {
                _workAreaEnforcer?.Stop();
                _workAreaEnforcer = null;
            }
        };
        _workAreaEnforcer.Start();
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
                    ShowMainShell();
                    break;
                case SystemIntegrationCommand.EnterTouchMode:
                    Services.ShellMode.EnterTouchMode();
                    break;
                case SystemIntegrationCommand.EnterDesktopMode:
                    Services.ShellMode.EnterDesktopMode();
                    break;
                case SystemIntegrationCommand.OpenSettings:
                    ShowMainShell(openSettings: true);
                    break;
                case SystemIntegrationCommand.EnterSafeMode:
                    Services.SystemIntegration.SetTaskbarVisible(true);
                    Services.ShellReplacement.RestoreExplorerForSession();
                    Window.Activate();
                    break;
                case SystemIntegrationCommand.Exit:
                    RestoreSystemShell();
                    DockWindow?.Close();
                    Window.Close();
                    break;
            }
        });
    }

    public static void ShowMainShell(bool openDrawer = false, bool openSettings = false, bool openControlCenter = false, bool openTaskSwitcher = false)
    {
        if (Window is not MainWindow mainWindow)
        {
            return;
        }

        Window.AppWindow.Show(true);
        Window.Activate();
        mainWindow.UseFullScreenShell();
        ShowStatusAndReserveWorkArea(homeMode: !openDrawer && !openSettings && !openControlCenter && !openTaskSwitcher);
        ShowDockWhenReady(homeMode: !openDrawer && !openSettings && !openControlCenter && !openTaskSwitcher);
        if (!openDrawer && !openSettings && !openControlCenter && !openTaskSwitcher)
        {
            mainWindow.ShowHome();
        }
        else if (openDrawer)
        {
            mainWindow.OpenDrawer();
        }
        else if (openSettings)
        {
            mainWindow.OpenSettings();
        }
        else if (openControlCenter)
        {
            mainWindow.OpenControlCenter();
        }
        else if (openTaskSwitcher)
        {
            mainWindow.OpenTaskSwitcher();
        }
    }

    private static void OnShellModeChanged(object? sender, ShellMode mode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (mode == ShellMode.Desktop)
            {
                Window.AppWindow.Show(true);
                if (Window is MainWindow desktopWindow)
                {
                    desktopWindow.UseFullScreenShell();
                    desktopWindow.ConfigureAsDesktopLayer();
                    KeepHomescreenBehindWindows();
                }

                ShowStatusAndReserveWorkArea(homeMode: false);
                ShowDockWhenReady(homeMode: false);
                Services.SystemIntegration.SetTaskbarVisible(false);
                return;
            }

            Window.AppWindow.Show(true);
            Window.Activate();
            ShowStatusAndReserveWorkArea(homeMode: true);
            ShowDockWhenReady(homeMode: true);
            if (Window is MainWindow mainWindow)
            {
                mainWindow.UseFullScreenShell();
                mainWindow.BringShellForward();
            }
        });
    }

    private static void RestoreSystemShell()
    {
        try
        {
            Services?.SystemIntegration.SetTaskbarVisible(true);
            Services?.SystemIntegration.RestoreWorkArea();
            Services?.ShellReplacement.RestoreExplorerForSession();
            _windowsKeyHook?.Dispose();
            _windowsKeyHook = null;
            _workAreaEnforcer?.Stop();
            _workAreaEnforcer = null;
            DockWindow?.Close();
            StatusWindow?.Close();
            Services?.SystemIntegration.Dispose();
        }
        catch
        {
            // Process shutdown must not be blocked by shell restoration cleanup.
        }
    }

    private static void SignalShellReadyIfNeeded()
    {
        var executablePath = Environment.ProcessPath ?? "CoreDesk.App.exe";
        if (Services.Options.ReplaceExplorerForSession || Services.ShellReplacement.IsConfiguredAsUserShell(executablePath))
        {
            Services.ShellReplacement.SignalShellReady();
        }
    }

    private static void ApplyShellTaskbarPolicy()
    {
        Services.SystemIntegration.SetTaskbarVisible(Services.Options.SafeMode || Services.Options.OverlayMode);
    }

    private static void KeepHomescreenBehindWindows()
    {
        if (Window is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.SendToDesktopLayer();
        var remainingTicks = 10;
        _desktopLayerEnforcer?.Stop();
        _desktopLayerEnforcer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _desktopLayerEnforcer.Tick += (_, _) =>
        {
            if (Window is MainWindow window)
            {
                window.SendToDesktopLayer();
            }

            remainingTicks--;
            if (remainingTicks <= 0)
            {
                _desktopLayerEnforcer?.Stop();
                _desktopLayerEnforcer = null;
            }
        };
        _desktopLayerEnforcer.Start();
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
