using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IUpdateService
{
    Version CurrentVersion { get; }

    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task StartUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
