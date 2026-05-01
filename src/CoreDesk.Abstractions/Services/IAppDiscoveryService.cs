using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IAppDiscoveryService
{
    Task<IReadOnlyList<AppEntry>> DiscoverAppsAsync(CancellationToken cancellationToken = default);
}

