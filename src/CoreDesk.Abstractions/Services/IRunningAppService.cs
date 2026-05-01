using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IRunningAppService
{
    Task<IReadOnlyList<RunningAppEntry>> GetRunningAppsAsync(CancellationToken cancellationToken = default);
}
