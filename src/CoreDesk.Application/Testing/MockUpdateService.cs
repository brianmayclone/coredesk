using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockUpdateService : IUpdateService
{
    public Version CurrentVersion { get; init; } = new(0, 1, 0);

    public UpdateInfo Update { get; init; } = new(new Version(0, 1, 0), new Version(0, 1, 1), true, "Mock update", new Uri("https://example.invalid/CoreDesk-Setup.exe"), null, null);

    public bool Started { get; private set; }

    public Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Update);
    }

    public Task StartUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Started = true;
        progress?.Report(1);
        return Task.CompletedTask;
    }
}
