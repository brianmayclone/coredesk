namespace CoreDesk.Abstractions.Models;

public sealed class WidgetDefinition
{
    public string Id { get; set; } = string.Empty;

    public string ProviderId { get; set; } = "coredesk";

    public string DisplayName { get; set; } = "Widget";

    public string Description { get; set; } = string.Empty;

    public string Kind { get; set; } = "custom";

    public int DefaultColumnSpan { get; set; } = 2;

    public int DefaultRowSpan { get; set; } = 1;

    public bool IsExternalProvider { get; set; }
}

public sealed class WidgetSnapshot
{
    public string WidgetId { get; set; } = string.Empty;

    public string Title { get; set; } = "Widget";

    public string PrimaryText { get; set; } = string.Empty;

    public string SecondaryText { get; set; } = string.Empty;

    public string? Glyph { get; set; }

    public string? AccentColor { get; set; }
}
