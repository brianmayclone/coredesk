using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.ViewModels;

public sealed partial class SettingsViewModel(
    IConfigurationStore configurationStore,
    IAutostartService autostartService,
    IDiagnosticsService diagnostics) : ObservableObject
{
    private CoreDeskSettings _settings = new();

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
}

