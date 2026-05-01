namespace CoreDesk.Abstractions.Models;

public sealed class HomeLayout
{
    public int SchemaVersion { get; set; } = 1;

    public List<HomePage> Pages { get; set; } = [HomePage.CreateDefault()];

    public List<string> DockAppIds { get; set; } = [];

    public List<AppFolder> Folders { get; set; } = [];

    public List<string> HiddenAppIds { get; set; } = [];

    public List<AppCategory> Categories { get; set; } = [];

    public List<HomeWidget> Widgets { get; set; } = [HomeWidget.Clock(), HomeWidget.Status()];
}

public sealed class HomePage
{
    public int Index { get; set; }

    public List<HomeTile> Tiles { get; set; } = [];

    public static HomePage CreateDefault() => new() { Index = 0 };
}

public sealed class HomeTile
{
    public string AppId { get; set; } = string.Empty;

    public int Column { get; set; }

    public int Row { get; set; }

    public string? FolderId { get; set; }
}

public sealed class AppFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Folder";

    public List<string> AppIds { get; set; } = [];

    public string Color { get; set; } = "#F4FFFFFF";

    public string? IconPath { get; set; }
}

public sealed class AppCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Apps";

    public List<string> AppIds { get; set; } = [];
}

public sealed class HomeWidget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ProviderId { get; set; } = "coredesk";

    public string Kind { get; set; } = "clock";

    public string Title { get; set; } = "Widget";

    public int Column { get; set; }

    public int Row { get; set; }

    public int ColumnSpan { get; set; } = 2;

    public int RowSpan { get; set; } = 1;

    public static HomeWidget Clock() => new()
    {
        Id = "clock-widget",
        Kind = "clock",
        Title = "Today",
        Column = 0,
        Row = 0,
        ColumnSpan = 2
    };

    public static HomeWidget Status() => new()
    {
        Id = "status-widget",
        Kind = "status",
        Title = "Device",
        Column = 2,
        Row = 0,
        ColumnSpan = 2
    };
}
