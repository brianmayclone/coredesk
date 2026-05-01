using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockAppLauncher(IDiagnosticsService diagnostics) : IAppLauncher
{
    public Task LaunchAsync(AppEntry app, CancellationToken cancellationToken = default)
    {
        diagnostics.Info($"Mock launch: {app.Id} / {app.DisplayName}");
        return Task.CompletedTask;
    }
}

