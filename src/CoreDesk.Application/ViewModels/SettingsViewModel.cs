using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.ViewModels;

public sealed partial class SettingsViewModel(
    IConfigurationStore configurationStore,
    IAutostartService autostartService,
    IUpdateService updateService,
    IDiagnosticsService diagnostics) : ObservableObject
{
    private CoreDeskSettings _settings = new();
    private UpdateInfo? _availableUpdate;

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    private ShellTheme _theme = ShellTheme.System;

    [ObservableProperty]
    private DockPosition _dockPosition = DockPosition.Bottom;

    [ObservableProperty]
    private bool _autostartEnabled;

    [ObservableProperty]
    private bool _hideTaskbarInTouchMode = true;

    [ObservableProperty]
    private bool _autoSwitchOnKeyboard = true;

    [ObservableProperty]
    private bool _reduceAnimations;

    [ObservableProperty]
    private string _installedVersion = updateService.CurrentVersion.ToString(3);

    [ObservableProperty]
    private string _updateStatus = "Ready to check for updates.";

    [ObservableProperty]
    private string _latestVersion = "-";

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private double _updateProgress;

    public bool CanInstallUpdate => IsUpdateAvailable && !IsCheckingForUpdates && !IsUpdating;

    public IReadOnlyList<string> Languages { get; } = ["en", "de"];

    public IReadOnlyList<ShellTheme> Themes { get; } = [ShellTheme.System, ShellTheme.Light, ShellTheme.Dark];

    public IReadOnlyList<DockPosition> DockPositions { get; } = [DockPosition.Bottom, DockPosition.Left, DockPosition.Right];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _settings = await configurationStore.LoadSettingsAsync(cancellationToken);
        Language = _settings.Language;
        Theme = _settings.Theme;
        DockPosition = _settings.DockPosition;
        AutostartEnabled = autostartService.IsEnabled() || _settings.AutoStartWithWindows;
        HideTaskbarInTouchMode = _settings.HideTaskbarInTouchMode;
        AutoSwitchOnKeyboard = _settings.AutoSwitchOnKeyboard;
        ReduceAnimations = _settings.Accessibility.ReduceAnimations;
        InstalledVersion = updateService.CurrentVersion.ToString(3);
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        _settings.Language = Language;
        _settings.Theme = Theme;
        _settings.DockPosition = DockPosition;
        _settings.AutoStartWithWindows = AutostartEnabled;
        _settings.HideTaskbarInTouchMode = HideTaskbarInTouchMode;
        _settings.AutoSwitchOnKeyboard = AutoSwitchOnKeyboard;
        _settings.Accessibility.ReduceAnimations = ReduceAnimations;

        await configurationStore.SaveSettingsAsync(_settings);
        autostartService.SetEnabled(AutostartEnabled, Environment.ProcessPath ?? "CoreDesk.App.exe");
        diagnostics.Info("Settings saved.");
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates || IsUpdating)
        {
            return;
        }

        try
        {
            IsCheckingForUpdates = true;
            UpdateStatus = "Checking GitHub releases...";
            _availableUpdate = await updateService.CheckForUpdatesAsync();
            LatestVersion = _availableUpdate.LatestVersion.ToString(3);
            IsUpdateAvailable = _availableUpdate.IsUpdateAvailable;
            UpdateStatus = IsUpdateAvailable
                ? $"CoreDesk {_availableUpdate.LatestVersion.ToString(3)} is available."
                : "CoreDesk is up to date.";
            diagnostics.Info($"Update check completed. Current={_availableUpdate.CurrentVersion}, Latest={_availableUpdate.LatestVersion}, Available={_availableUpdate.IsUpdateAvailable}");
        }
        catch (Exception exception)
        {
            UpdateStatus = "Update check failed. Try again later.";
            diagnostics.Error(exception, "Update check failed.");
        }
        finally
        {
            IsCheckingForUpdates = false;
            OnPropertyChanged(nameof(CanInstallUpdate));
        }
    }

    [RelayCommand]
    public async Task InstallUpdateAsync()
    {
        if (_availableUpdate is null || !CanInstallUpdate)
        {
            return;
        }

        try
        {
            IsUpdating = true;
            UpdateProgress = 0;
            UpdateStatus = "Downloading update...";
            var progress = new Progress<double>(value =>
            {
                UpdateProgress = Math.Round(value * 100);
                UpdateStatus = value >= 1 ? "Starting installer..." : $"Downloading update... {UpdateProgress:0}%";
            });
            await updateService.StartUpdateAsync(_availableUpdate, progress);
        }
        catch (Exception exception)
        {
            UpdateStatus = "Update failed. Check logs for details.";
            diagnostics.Error(exception, "Update installation failed.");
        }
        finally
        {
            IsUpdating = false;
            OnPropertyChanged(nameof(CanInstallUpdate));
        }
    }

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstallUpdate));
    }

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstallUpdate));
    }

    partial void OnIsUpdatingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstallUpdate));
    }
}
