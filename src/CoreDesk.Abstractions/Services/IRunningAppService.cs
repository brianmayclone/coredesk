using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IRunningAppService
{
    Task<IReadOnlyList<RunningAppEntry>> GetRunningAppsAsync(CancellationToken cancellationToken = default);

    Task<bool> TryActivateAsync(AppEntry app, CancellationToken cancellationToken = default);
}
