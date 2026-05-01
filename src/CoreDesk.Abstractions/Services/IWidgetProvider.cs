using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IWidgetProvider
{
    string ProviderId { get; }

    Task<IReadOnlyList<WidgetDefinition>> GetAvailableWidgetsAsync(CancellationToken cancellationToken = default);

    Task<WidgetSnapshot> GetSnapshotAsync(HomeWidget widget, CoreDesk.Abstractions.Models.SystemStatus status, CancellationToken cancellationToken = default);
}
