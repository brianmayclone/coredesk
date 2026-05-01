using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;
using CoreDesk.Application.Diagnostics;
using CoreDesk.Application.Localization;
using CoreDesk.Application.Search;
using CoreDesk.Application.Shell;
using CoreDesk.Application.SystemStatus;
using CoreDesk.Application.Testing;
using CoreDesk.Application.Updates;
using CoreDesk.Application.ViewModels;
using CoreDesk.Application.Widgets;
using CoreDesk.Persistence;
using CoreDesk.Windows.AppDiscovery;
using CoreDesk.Windows.AppLaunching;
using CoreDesk.Windows.Hardware;
using CoreDesk.Windows.Integration;
using CoreDesk.Windows.Status;

namespace CoreDesk_App;

public sealed class AppComposition : IDisposable
{
    public AppComposition(LaunchOptions options)
    {
        Options = options;
        Diagnostics = new FileDiagnosticsService(options, FindRepositoryRoot());
        Localization = new DictionaryLocalizationService();
        ConfigurationStore = new JsonConfigurationStore();

        var useMocks = options.MockHardware;
        SystemIntegration = useMocks
            ? new MockSystemIntegrationService(Diagnostics)
            : new WindowsSystemIntegrationService();
        AppDiscovery = useMocks ? new MockAppDiscoveryService() : new StartMenuAppDiscoveryService();
        AppLauncher = useMocks ? new MockAppLauncher(Diagnostics) : new WindowsAppLauncher();
        RunningApps = useMocks ? new MockRunningAppService() : new WindowsRunningAppService();
        Updates = useMocks ? new MockUpdateService() : new GitHubUpdateService(GetUpdateRepository(), Diagnostics);
        HardwareMonitor = useMocks ? new MockHardwareMonitor() : new PollingHardwareMonitor();
        PowerStatus = useMocks ? new MockPowerStatusService() : new WindowsPowerStatusService();
        NetworkStatus = useMocks ? new MockNetworkStatusService() : new WindowsNetworkStatusService();
        Wallpaper = useMocks ? new MockWallpaperService() : new WindowsWallpaperService();
        DisplayMetrics = useMocks ? new MockDisplayMetricsService() : new WindowsDisplayMetricsService();
        SystemStatus = new SystemStatusService(PowerStatus, NetworkStatus);
        WidgetRegistry = new WidgetRegistry([new CoreDeskWidgetProvider(), new WindowsWidgetBridgeProvider()]);
        Autostart = useMocks ? new MockAutostartService() : new RegistryAutostartService();
        ShellMode = new ShellModeService(SystemIntegration);
        AppSearch = new AppSearchService();
    }

    public LaunchOptions Options { get; }

    public ILocalizationService Localization { get; }

    public IConfigurationStore ConfigurationStore { get; }

    public ISystemIntegrationService SystemIntegration { get; }

    public IShellModeService ShellMode { get; }

    public IAppDiscoveryService AppDiscovery { get; }

    public IAppLauncher AppLauncher { get; }

    public IRunningAppService RunningApps { get; }

    public IUpdateService Updates { get; }

    public IHardwareMonitor HardwareMonitor { get; }

    public IPowerStatusService PowerStatus { get; }

    public INetworkStatusService NetworkStatus { get; }

    public IWallpaperService Wallpaper { get; }

    public IDisplayMetricsService DisplayMetrics { get; }

    public ISystemStatusService SystemStatus { get; }

    public IWidgetRegistry WidgetRegistry { get; }

    public IAutostartService Autostart { get; }

    public IDiagnosticsService Diagnostics { get; }

    public IAppSearchService AppSearch { get; }

    public ShellViewModel CreateShellViewModel()
    {
        var settings = new SettingsViewModel(ConfigurationStore, Autostart, Updates, Diagnostics);
        return new ShellViewModel(
            Localization,
            AppDiscovery,
            AppLauncher,
            RunningApps,
            ConfigurationStore,
            ShellMode,
            AppSearch,
            SystemStatus,
            WidgetRegistry,
            HardwareMonitor,
            Wallpaper,
            DisplayMetrics,
            settings,
            Options,
            Diagnostics);
    }

    public void Dispose()
    {
        HardwareMonitor.Dispose();
        SystemIntegration.Dispose();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CoreDesk.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreDesk");
    }

    private static string GetUpdateRepository()
    {
        return Environment.GetEnvironmentVariable("COREDESK_UPDATE_REPOSITORY")
            ?? "brianmayclone/coredesk";
    }
}
