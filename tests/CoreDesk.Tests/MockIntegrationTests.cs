using CoreDesk.Abstractions.Models;
using CoreDesk.Application.Diagnostics;
using CoreDesk.Application.Testing;

namespace CoreDesk.Tests;

public sealed class MockIntegrationTests
{
    [Fact]
    public void MockSystemIntegration_RaisesTrayCommands()
    {
        var diagnostics = new FileDiagnosticsService(new LaunchOptions { Diagnostics = true });
        var integration = new MockSystemIntegrationService(diagnostics);
        SystemIntegrationCommand? requested = null;
        integration.CommandRequested += (_, command) => requested = command;

        integration.Request(SystemIntegrationCommand.OpenSettings);

        Assert.Equal(SystemIntegrationCommand.OpenSettings, requested);
    }

    [Fact]
    public void MockAutostartService_RoundTripsEnabledState()
    {
        var autostart = new MockAutostartService();

        autostart.SetEnabled(true, "CoreDesk.App.exe", "--safe-mode");

        Assert.True(autostart.IsEnabled());
        Assert.Equal("CoreDesk.App.exe", autostart.ExecutablePath);
        Assert.Equal("--safe-mode", autostart.Arguments);
    }
}

