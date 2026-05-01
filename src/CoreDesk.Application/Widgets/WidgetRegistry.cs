using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Widgets;

public sealed class WidgetRegistry(IEnumerable<IWidgetProvider> providers) : IWidgetRegistry
{
    private readonly IReadOnlyList<IWidgetProvider> _providers = [.. providers];

    public async Task<IReadOnlyList<WidgetDefinition>> GetAvailableWidgetsAsync(CancellationToken cancellationToken = default)
    {
        var widgets = new List<WidgetDefinition>();
        foreach (var provider in _providers)
        {
            widgets.AddRange(await provider.GetAvailableWidgetsAsync(cancellationToken));
        }

        return widgets
            .OrderBy(widget => widget.IsExternalProvider)
            .ThenBy(widget => widget.DisplayName)
            .ToList();
    }

    public async Task<WidgetSnapshot> GetSnapshotAsync(HomeWidget widget, CoreDesk.Abstractions.Models.SystemStatus status, CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(candidate =>
            widget.ProviderId.Equals(candidate.ProviderId, StringComparison.OrdinalIgnoreCase));

        if (provider is not null)
        {
            return await provider.GetSnapshotAsync(widget, status, cancellationToken);
        }

        var fallback = _providers.FirstOrDefault(candidate => candidate.ProviderId == "coredesk") ?? _providers.First();
        return await fallback.GetSnapshotAsync(widget, status, cancellationToken);
    }
}
