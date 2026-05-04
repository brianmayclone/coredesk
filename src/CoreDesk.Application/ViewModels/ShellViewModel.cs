using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;
using CoreDesk.Application.AppClassification;
using CoreDesk.Application.Dock;
using CoreDesk.Application.Layout;

namespace CoreDesk.Application.ViewModels;

public sealed partial class ShellViewModel(
    ILocalizationService text,
    IAppDiscoveryService appDiscovery,
    IAppLauncher appLauncher,
    IRunningAppService runningAppService,
    IConfigurationStore configurationStore,
    IShellModeService shellModeService,
    IAppSearchService appSearch,
    ISystemStatusService systemStatusService,
    IWidgetRegistry widgetRegistry,
    IHardwareMonitor hardwareMonitor,
    IWallpaperService wallpaperService,
    IDisplayMetricsService displayMetricsService,
    SettingsViewModel settings,
    LaunchOptions launchOptions,
    IDiagnosticsService diagnostics) : ObservableObject
{
    private List<AppEntry> _allApps = [];
    private HomeLayout _layout = new();
    private readonly DefaultLayoutBuilder _defaultLayoutBuilder = new();
    private DisplayMetrics _displayMetrics = new(1920, 1080, 96, 96, null, null);

    public ObservableCollection<AppEntry> HomeApps { get; } = [];

    public ObservableCollection<FolderTileViewModel> HomeFolders { get; } = [];

    public ObservableCollection<HomeTileViewModel> HomeTiles { get; } = [];

    public ObservableCollection<HomeWidgetViewModel> Widgets { get; } = [];

    public ObservableCollection<AppEntry> DockApps { get; } = [];

    public ObservableCollection<DockItemViewModel> PinnedDockItems { get; } = [];

    public ObservableCollection<DockItemViewModel> RunningDockItems { get; } = [];

    public ObservableCollection<DockItemViewModel> TaskSwitcherItems { get; } = [];

    public ObservableCollection<AppEntry> DrawerApps { get; } = [];

    public ObservableCollection<DrawerCategoryViewModel> DrawerCategories { get; } = [];

    public ObservableCollection<AppEntry> OpenFolderApps { get; } = [];

    public SettingsViewModel Settings { get; } = settings;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDrawerOpen;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isControlCenterOpen;

    [ObservableProperty]
    private ShellMode _currentMode = ShellMode.Touch;

    [ObservableProperty]
    private bool _isDockVisible = true;

    [ObservableProperty]
    private bool _isTaskSwitcherOpen;

    [ObservableProperty]
    private string _currentTime = DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    private string _batteryLabel = "Battery --";

    [ObservableProperty]
    private string _networkLabel = "Offline";

    [ObservableProperty]
    private string _keyboardLabel = "Touch";

    [ObservableProperty]
    private FolderTileViewModel? _openFolderTile;

    [ObservableProperty]
    private int _currentPageIndex;

    public bool IsFolderOpen => OpenFolderTile is not null;

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchText);

    public int PageCount => Math.Max(1, _layout.Pages.Count);

    public string AppTitle => text["AppTitle"];

    public string SearchPlaceholder => text["SearchPlaceholder"];

    public string AppDrawerLabel => text["AppDrawer"];

    public string SettingsLabel => text["Settings"];

    public string DesktopLabel => text["Desktop"];

    public string ModeLabel => CurrentMode == ShellMode.Touch ? text["TouchMode"] : text["DesktopMode"];

    public bool IsDesktopMode => CurrentMode == ShellMode.Desktop;

    public bool IsTouchMode => CurrentMode == ShellMode.Touch;

    public string ControlCenterLabel => text["ControlCenter"];

    public string? WallpaperPath { get; private set; }

    public string AppCountLabel => $"{_allApps.Count} apps indexed";

    public string DisplaySummary => $"{_displayMetrics.PixelWidth} x {_displayMetrics.PixelHeight} px";

    public string DpiSummary => $"{_displayMetrics.DpiX:0.#} DPI";

    public double UiScale { get; private set; } = 1;

    public double ShellPadding { get; private set; } = 42;

    public double AppTileWidth { get; private set; } = 132;

    public double AppTileHeight { get; private set; } = 152;

    public double AppIconSize { get; private set; } = 86;

    public double AppIconGlyphSize { get; private set; } = 34;

    public double DockButtonSize { get; private set; } = 60;

    public double DockIconSize { get; private set; } = 26;

    public double DrawerTileWidth { get; private set; } = 156;

    public double DrawerTileHeight { get; private set; } = 146;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _ = widgetRegistry;
        diagnostics.Info("ShellViewModel initialization started.");
        if (launchOptions.ResetConfig)
        {
            await configurationStore.ResetAsync(cancellationToken);
            diagnostics.Info("Configuration reset requested.");
        }

        var settings = await configurationStore.LoadSettingsAsync(cancellationToken);
        text.SetLanguage(launchOptions.LanguageOverride ?? settings.Language);
        await Settings.LoadAsync(cancellationToken);
        WallpaperPath = wallpaperService.GetCurrentWallpaperPath();
        _displayMetrics = displayMetricsService.GetPrimaryDisplayMetrics();
        diagnostics.Info($"Wallpaper path: {WallpaperPath ?? "<none>"}");
        var diagonalLabel = _displayMetrics.DiagonalInches > 0
            ? _displayMetrics.DiagonalInches.ToString("0.#")
            : "unknown";
        diagnostics.Info($"Primary display: {_displayMetrics.PixelWidth}x{_displayMetrics.PixelHeight}px at {_displayMetrics.DpiX:0.#}x{_displayMetrics.DpiY:0.#} DPI, diagonal {diagonalLabel} inches.");
        ApplyAdaptiveSizing();

        _allApps = [.. (await appDiscovery.DiscoverAppsAsync(cancellationToken)).OrderBy(app => app.DisplayName)];
        _layout = await configurationStore.LoadLayoutAsync(cancellationToken);
        _defaultLayoutBuilder.EnsureDefaultLayout(_layout, _allApps);
        await configurationStore.SaveLayoutAsync(_layout, cancellationToken);
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, PageCount - 1);
        CurrentMode = shellModeService.CurrentMode;
        RefreshStatus();
        await RefreshAppCollectionsAsync(cancellationToken);

        shellModeService.ModeChanged += (_, mode) =>
        {
            CurrentMode = mode;
            IsDockVisible = true;
            IsTaskSwitcherOpen = false;
            OnPropertyChanged(nameof(ModeLabel));
            OnPropertyChanged(nameof(IsDesktopMode));
            OnPropertyChanged(nameof(IsTouchMode));
            RefreshStatus();
        };
        hardwareMonitor.HardwareStateChanged += (_, _) => RefreshStatus();
        diagnostics.Info($"ShellViewModel initialized with {_allApps.Count} apps.");
        OnPropertyChanged(nameof(AppCountLabel));
        OnPropertyChanged(nameof(DisplaySummary));
        OnPropertyChanged(nameof(DpiSummary));
        OnPropertyChanged(nameof(PageCount));
    }

    public void UpdateViewport(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var diagonal = _displayMetrics.DiagonalInches;
        var physicalBias = diagonal is > 0 and < 14 ? 1.12 : 1.0;
        var widthScale = Math.Clamp(width / 1700.0, 0.92, 1.34);
        var heightScale = Math.Clamp(height / 950.0, 0.92, 1.24);
        UiScale = Math.Round(Math.Min(widthScale, heightScale) * physicalBias, 2);
        ApplyAdaptiveSizing();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshDrawer();
        OnPropertyChanged(nameof(IsSearchActive));
    }

    partial void OnCurrentPageIndexChanged(int value)
    {
        RefreshHomeTiles();
        OnPropertyChanged(nameof(PageCount));
    }

    [RelayCommand]
    private void OpenDrawer()
    {
        IsDrawerOpen = true;
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void CloseDrawer()
    {
        IsDrawerOpen = false;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsSettingsOpen = true;
        IsDrawerOpen = false;
    }

    [RelayCommand]
    private void OpenControlCenter()
    {
        IsDockVisible = true;
        IsControlCenterOpen = true;
        IsDrawerOpen = false;
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void OpenFolder(FolderTileViewModel? folder)
    {
        OpenFolderTile = folder;
    }

    [RelayCommand]
    private void CloseFolder()
    {
        OpenFolderTile = null;
    }

    partial void OnOpenFolderTileChanged(FolderTileViewModel? value)
    {
        OpenFolderApps.Clear();
        if (value is not null)
        {
            foreach (var app in AppsByIds(value.Folder.AppIds))
            {
                OpenFolderApps.Add(app);
            }
        }

        OnPropertyChanged(nameof(IsFolderOpen));
    }

    [RelayCommand]
    private void CloseControlCenter()
    {
        IsControlCenterOpen = false;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPageIndex < PageCount - 1)
        {
            CurrentPageIndex++;
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
        }
    }

    [RelayCommand]
    private void ToggleDesktop()
    {
        if (CurrentMode == ShellMode.Touch)
        {
            shellModeService.EnterDesktopMode();
        }
        else
        {
            shellModeService.EnterTouchMode();
        }
    }

    [RelayCommand]
    private void ShowDock()
    {
        IsDockVisible = true;
    }

    [RelayCommand]
    private void HideDock()
    {
        if (CurrentMode == ShellMode.Desktop && !IsTaskSwitcherOpen && !IsControlCenterOpen && !IsDrawerOpen && !IsSettingsOpen)
        {
            IsDockVisible = false;
        }
    }

    [RelayCommand]
    private void OpenTaskSwitcher()
    {
        IsDockVisible = true;
        IsTaskSwitcherOpen = true;
        IsControlCenterOpen = false;
        IsDrawerOpen = false;
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void CloseTaskSwitcher()
    {
        IsTaskSwitcherOpen = false;
    }

    [RelayCommand]
    private async Task LaunchAppAsync(AppEntry? app)
    {
        if (app is null)
        {
            return;
        }

        await appLauncher.LaunchAsync(app);
        diagnostics.Info($"Launched app: {app.Id}");
        await RefreshRunningAppsAsync();
    }

    [RelayCommand]
    private async Task OpenDockItemAsync(DockItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsRunning && await runningAppService.TryActivateAsync(item.App))
        {
            diagnostics.Info($"Activated running app: {item.App.Id}");
            await RefreshRunningAppsAsync();
            return;
        }

        await LaunchAppAsync(item.App);
    }

    public void Tick()
    {
        CurrentTime = DateTime.Now.ToString("HH:mm");
        RefreshStatus();
        _ = RefreshRunningAppsAsync();
    }

    public async Task RefreshInstalledAppsAsync(CancellationToken cancellationToken = default)
    {
        List<AppEntry> discovered = [.. (await appDiscovery.DiscoverAppsAsync(cancellationToken)).OrderBy(app => app.DisplayName)];
        var oldIds = _allApps.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newIds = discovered.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (oldIds.SetEquals(newIds))
        {
            await RefreshRunningAppsAsync(cancellationToken);
            return;
        }

        _allApps = discovered;
        _defaultLayoutBuilder.EnsureDefaultLayout(_layout, _allApps);
        await RefreshAppCollectionsAsync(cancellationToken);
        await RefreshRunningAppsAsync(cancellationToken);
        OnPropertyChanged(nameof(AppCountLabel));
        diagnostics.Info($"App index refreshed. Apps: {_allApps.Count}");
    }

    private void RefreshStatus()
    {
        var status = systemStatusService.GetStatus(CurrentMode, hardwareMonitor.Current.IsKeyboardPresent);
        BatteryLabel = status.BatteryPercent is null
            ? "Battery --"
            : status.IsCharging ? $"Battery {status.BatteryPercent}% charging" : $"Battery {status.BatteryPercent}%";
        NetworkLabel = status.IsNetworkAvailable ? "Online" : "Offline";
        KeyboardLabel = status.IsKeyboardPresent ? "Keyboard" : "Touch";
    }

    private async Task RefreshAppCollectionsAsync(CancellationToken cancellationToken = default)
    {
        HomeApps.Clear();
        HomeFolders.Clear();
        HomeTiles.Clear();
        Widgets.Clear();
        DockApps.Clear();
        PinnedDockItems.Clear();
        RunningDockItems.Clear();
        TaskSwitcherItems.Clear();

        foreach (var widget in _layout.Widgets)
        {
            Widgets.Add(widget.Kind.Equals("clock", StringComparison.OrdinalIgnoreCase)
                ? new HomeWidgetViewModel(widget, DateTime.Now.ToString("HH:mm"), DateTime.Now.ToString("dddd, MMM d"))
                : new HomeWidgetViewModel(widget, BatteryLabel, $"{NetworkLabel} · {KeyboardLabel}"));
        }

        foreach (var folder in _layout.Folders)
        {
            var folderTile = new FolderTileViewModel(folder, folder.AppIds.Count, AppsByIds(folder.AppIds).Take(4).ToList());
            HomeFolders.Add(folderTile);
        }

        RefreshHomeTiles();

        foreach (var app in AppsByIds(_layout.DockAppIds).Take(8))
        {
            DockApps.Add(app);
        }

        RefreshDrawer();
        await RefreshRunningAppsAsync(cancellationToken);
    }

    private void ApplyAdaptiveSizing()
    {
        ShellPadding = Math.Round(42 * UiScale);
        AppTileWidth = Math.Round(132 * UiScale);
        AppTileHeight = Math.Round(152 * UiScale);
        AppIconSize = Math.Round(Math.Clamp(82 * UiScale, 76, 122));
        AppIconGlyphSize = Math.Round(AppIconSize * 0.4);
        DockButtonSize = Math.Round(Math.Clamp(60 * UiScale, 58, 86));
        DockIconSize = Math.Round(DockButtonSize * 0.44);
        DrawerTileWidth = Math.Round(156 * UiScale);
        DrawerTileHeight = Math.Round(146 * UiScale);

        OnPropertyChanged(nameof(UiScale));
        OnPropertyChanged(nameof(ShellPadding));
        OnPropertyChanged(nameof(AppTileWidth));
        OnPropertyChanged(nameof(AppTileHeight));
        OnPropertyChanged(nameof(AppIconSize));
        OnPropertyChanged(nameof(AppIconGlyphSize));
        OnPropertyChanged(nameof(DockButtonSize));
        OnPropertyChanged(nameof(DockIconSize));
        OnPropertyChanged(nameof(DrawerTileWidth));
        OnPropertyChanged(nameof(DrawerTileHeight));
        OnPropertyChanged(nameof(WallpaperPath));
        OnPropertyChanged(nameof(DisplaySummary));
        OnPropertyChanged(nameof(DpiSummary));
    }

    private void RefreshHomeTiles()
    {
        HomeApps.Clear();
        HomeTiles.Clear();

        var appById = _allApps.ToDictionary(app => app.Id, StringComparer.OrdinalIgnoreCase);
        var folderById = HomeFolders.ToDictionary(folder => folder.Folder.Id, StringComparer.OrdinalIgnoreCase);
        var page = _layout.Pages
            .OrderBy(candidate => candidate.Index)
            .FirstOrDefault(candidate => candidate.Index == CurrentPageIndex)
            ?? _layout.Pages.OrderBy(candidate => candidate.Index).FirstOrDefault();

        if (page is null)
        {
            return;
        }

        foreach (var tile in page.Tiles.OrderBy(tile => tile.Row).ThenBy(tile => tile.Column))
        {
            if (!string.IsNullOrWhiteSpace(tile.AppId) && appById.TryGetValue(tile.AppId, out var app))
            {
                HomeApps.Add(app);
                HomeTiles.Add(new HomeTileViewModel(app, null));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(tile.FolderId) && folderById.TryGetValue(tile.FolderId, out var folder))
            {
                HomeTiles.Add(new HomeTileViewModel(null, folder));
            }
        }
    }

    private void RefreshDrawer()
    {
        DrawerApps.Clear();
        var query = SearchText.Trim();
        var apps = string.IsNullOrWhiteSpace(query)
            ? _allApps
            : appSearch.Search(_allApps, query);

        foreach (var app in apps)
        {
            DrawerApps.Add(app);
        }
    }

    private async Task RefreshRunningAppsAsync(CancellationToken cancellationToken = default)
    {
        var runningApps = await runningAppService.GetRunningAppsAsync(cancellationToken);
        var pinnedApps = AppsByIds(_layout.DockAppIds).Take(8).ToList();
        var runningIds = DockRunningAppMatcher.MatchRunningAppIds(runningApps, _allApps, pinnedApps);
        var pinnedIds = pinnedApps.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        PinnedDockItems.Clear();
        RunningDockItems.Clear();
        TaskSwitcherItems.Clear();
        DockApps.Clear();

        foreach (var app in pinnedApps)
        {
            var running = runningApps.FirstOrDefault(candidate => DockRunningAppMatcher.IsRunningMatch(app, candidate));
            var isRunning = running is not null || runningIds.Contains(app.Id);
            PinnedDockItems.Add(CreateDockItem(app, isRunning, running));
            DockApps.Add(app);
            if (isRunning)
            {
                TaskSwitcherItems.Add(CreateDockItem(app, true, running));
            }
        }

        foreach (var app in _allApps.Where(app => runningIds.Contains(app.Id) && !pinnedIds.Contains(app.Id)).Take(6))
        {
            var running = runningApps.FirstOrDefault(candidate => DockRunningAppMatcher.IsRunningMatch(app, candidate));
            var item = CreateDockItem(app, true, running);
            RunningDockItems.Add(item);
            TaskSwitcherItems.Add(item);
        }
    }

    private static DockItemViewModel CreateDockItem(AppEntry app, bool isRunning, RunningAppEntry? running)
    {
        return new DockItemViewModel(app, isRunning, running?.WindowTitle, running?.PreviewPath);
    }

    private IEnumerable<AppEntry> AppsByIds(IEnumerable<string> ids)
    {
        var byId = _allApps.ToDictionary(app => app.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var app))
            {
                yield return app;
            }
        }
    }
}
