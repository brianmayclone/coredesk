using CoreDesk.Abstractions.Models;
using System.Runtime.CompilerServices;

namespace CoreDesk.Abstractions.Services;

public interface IAppDiscoveryService
{
    Task<IReadOnlyList<AppEntry>> DiscoverAppsAsync(CancellationToken cancellationToken = default);

    async IAsyncEnumerable<AppEntry> DiscoverAppsIncrementalAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var app in await DiscoverAppsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return app;
        }
    }
}
