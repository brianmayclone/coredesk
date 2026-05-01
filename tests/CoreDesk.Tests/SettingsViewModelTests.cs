using CoreDesk.Abstractions.Models;
using CoreDesk.Application.Diagnostics;
using CoreDesk.Application.Testing;
using CoreDesk.Application.ViewModels;
using CoreDesk.Persistence;

namespace CoreDesk.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task SaveCommand_PersistsSettingsAndAutostartState()
    {
        var root = Path.Combine(Path.GetTempPath(), "CoreDesk.Tests", Guid.NewGuid().ToString("N"));
        var store = new JsonConfigurationStore(root);
        var autostart = new MockAutostartService();
        var updates = new MockUpdateService();
        var diagnostics = new FileDiagnosticsService(new LaunchOptions { Diagnostics = true });
        var viewModel = new SettingsViewModel(store, autostart, updates, diagnostics);

        await viewModel.LoadAsync();
        viewModel.Language = "de";
        viewModel.Theme = ShellTheme.Dark;
        viewModel.DockPosition = DockPosition.Left;
        viewModel.AutostartEnabled = true;
        await viewModel.SaveCommand.ExecuteAsync(null);

        var settings = await store.LoadSettingsAsync();
        Assert.Equal("de", settings.Language);
        Assert.Equal(ShellTheme.Dark, settings.Theme);
        Assert.Equal(DockPosition.Left, settings.DockPosition);
        Assert.True(settings.AutoStartWithWindows);
        Assert.True(autostart.IsEnabled());
    }

    [Fact]
    public async Task CheckForUpdatesCommand_ExposesAvailableUpdate()
    {
        var root = Path.Combine(Path.GetTempPath(), "CoreDesk.Tests", Guid.NewGuid().ToString("N"));
        var store = new JsonConfigurationStore(root);
        var autostart = new MockAutostartService();
        var updates = new MockUpdateService();
        var diagnostics = new FileDiagnosticsService(new LaunchOptions { Diagnostics = true });
        var viewModel = new SettingsViewModel(store, autostart, updates, diagnostics);

        await viewModel.LoadAsync();
        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.Equal("0.1.1", viewModel.LatestVersion);
        Assert.True(viewModel.CanInstallUpdate);
    }
}
