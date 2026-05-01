using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Widgets;

public sealed class WindowsWidgetBridgeProvider : IWidgetProvider
{
    public string ProviderId => "windows-widget-bridge";

    public Task<IReadOnlyList<WidgetDefinition>> GetAvailableWidgetsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WidgetDefinition> widgets =
        [
            new()
            {
                Id = "windows.widgets.placeholder",
                ProviderId = ProviderId,
                DisplayName = "Windows Widgets",
                Description = "Bridge placeholder for registered Windows Widget providers where Microsoft exposes provider data.",
                Kind = "windows-widgets",
                DefaultColumnSpan = 2,
                DefaultRowSpan = 1,
                IsExternalProvider = true
            }
        ];

        return Task.FromResult(widgets);
    }

    public Task<WidgetSnapshot> GetSnapshotAsync(HomeWidget widget, CoreDesk.Abstractions.Models.SystemStatus status, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new WidgetSnapshot
        {
            WidgetId = widget.Id,
            Title = widget.Title,
            PrimaryText = "Windows Widgets",
            SecondaryText = "Provider bridge pending",
            Glyph = "\uE739"
        });
    }
}
