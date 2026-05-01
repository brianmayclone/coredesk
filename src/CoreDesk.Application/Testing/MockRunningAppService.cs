using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockRunningAppService : IRunningAppService
{
    public AppEntry? LastActivatedApp { get; private set; }

    public Task<IReadOnlyList<RunningAppEntry>> GetRunningAppsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RunningAppEntry> apps =
        [
            new("mock-browser", null, "Browser"),
            new("mock-files", null, "File Explorer")
        ];

        return Task.FromResult(apps);
    }

    public Task<bool> TryActivateAsync(AppEntry app, CancellationToken cancellationToken = default)
    {
        LastActivatedApp = app;
        return Task.FromResult(true);
    }
}
