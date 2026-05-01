using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IConfigurationStore
{
    Task<CoreDeskSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(CoreDeskSettings settings, CancellationToken cancellationToken = default);

    Task<HomeLayout> LoadLayoutAsync(CancellationToken cancellationToken = default);

    Task SaveLayoutAsync(HomeLayout layout, CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);

    Task ExportAsync(string targetDirectory, CancellationToken cancellationToken = default);

    Task ImportAsync(string sourceDirectory, CancellationToken cancellationToken = default);
}
