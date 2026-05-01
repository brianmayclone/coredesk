using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockRunningAppService : IRunningAppService
{
    public Task<IReadOnlyList<RunningAppEntry>> GetRunningAppsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RunningAppEntry> apps =
        [
            new("mock-browser", null, "Browser"),
            new("mock-files", null, "File Explorer")
        ];

        return Task.FromResult(apps);
    }
}
