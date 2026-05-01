using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IAppLauncher
{
    Task LaunchAsync(AppEntry app, CancellationToken cancellationToken = default);
}

