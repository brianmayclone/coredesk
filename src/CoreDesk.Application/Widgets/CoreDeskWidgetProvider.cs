using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Widgets;

public sealed class CoreDeskWidgetProvider : IWidgetProvider
{
    public string ProviderId => "coredesk";

    public Task<IReadOnlyList<WidgetDefinition>> GetAvailableWidgetsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WidgetDefinition> widgets =
        [
            Create("clock", "Clock", "Time, date and day", 2, 1),
            Create("status", "Device Status", "Battery, network and input state", 2, 1),
            Create("battery", "Battery", "Battery percentage and charging state", 1, 1),
            Create("network", "Network", "Connection state and quick network context", 1, 1),
            Create("calendar", "Calendar", "Today and upcoming agenda placeholder", 2, 1),
            Create("launcher", "Quick Launch", "Pinned utilities and shell actions", 2, 1),
            Create("weather", "Weather", "Weather provider placeholder", 2, 1),
            Create("notes", "Notes", "Small touch note surface placeholder", 2, 1)
        ];

        return Task.FromResult(widgets);
    }

    public Task<WidgetSnapshot> GetSnapshotAsync(HomeWidget widget, CoreDesk.Abstractions.Models.SystemStatus status, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var snapshot = widget.Kind.ToLowerInvariant() switch
        {
            "clock" => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = now.ToString("HH:mm"),
                SecondaryText = now.ToString("dddd, MMM d"),
                Glyph = "\uE121"
            },
            "status" => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = status.BatteryPercent is null ? "Battery --" : status.IsCharging ? $"Battery {status.BatteryPercent}% charging" : $"Battery {status.BatteryPercent}%",
                SecondaryText = $"{(status.IsNetworkAvailable ? "Online" : "Offline")} · {(status.IsKeyboardPresent ? "Keyboard" : "Touch")}",
                Glyph = "\uE7F4"
            },
            "battery" => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = status.BatteryPercent is null ? "--%" : $"{status.BatteryPercent}%",
                SecondaryText = status.IsCharging ? "Charging" : "On battery",
                Glyph = "\uEBAA"
            },
            "network" => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = status.IsNetworkAvailable ? "Online" : "Offline",
                SecondaryText = "Network",
                Glyph = "\uE701"
            },
            "calendar" => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = now.ToString("dddd"),
                SecondaryText = now.ToString("MMMM d"),
                Glyph = "\uE787"
            },
            "launcher" => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = "Quick Launch",
                SecondaryText = "Desktop · Drawer · Settings",
                Glyph = "\uE71D"
            },
            "weather" => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = "Weather",
                SecondaryText = "Provider not connected",
                Glyph = "\uE706"
            },
            "notes" => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = "Notes",
                SecondaryText = "Tap to capture an idea",
                Glyph = "\uE70B"
            },
            _ => new WidgetSnapshot
            {
                WidgetId = widget.Id,
                Title = widget.Title,
                PrimaryText = widget.Title,
                SecondaryText = widget.Kind,
                Glyph = "\uECAA"
            }
        };

        return Task.FromResult(snapshot);
    }

    private static WidgetDefinition Create(string kind, string name, string description, int columns, int rows)
    {
        return new WidgetDefinition
        {
            Id = $"coredesk.{kind}",
            ProviderId = "coredesk",
            DisplayName = name,
            Description = description,
            Kind = kind,
            DefaultColumnSpan = columns,
            DefaultRowSpan = rows
        };
    }
}
