using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.Layout;

public sealed class DefaultLayoutBuilder
{
    private static readonly string[] HomePreferredNames =
    [
        "Microsoft Edge",
        "Google Chrome",
        "Edge",
        "File Explorer",
        "CoreDesk Settings",
        "Settings",
        "Microsoft Store",
        "Store",
        "Photos",
        "Camera",
        "Mail",
        "Calendar",
        "Outlook",
        "OneNote",
        "Word",
        "Excel",
        "PowerPoint",
        "Paint",
        "Notepad",
        "Calculator",
        "Terminal",
        "Browser",
        "Music",
        "Maps",
        "Notes"
    ];

    private static readonly string[] UtilityNames =
    [
        "Notepad",
        "Calculator",
        "Paint",
        "Terminal",
        "PowerShell",
        "Windows PowerShell",
        "Command Prompt",
        "Snipping Tool"
    ];

    private static readonly string[] DockNames =
    [
        "Microsoft Edge",
        "Edge",
        "Browser",
        "File Explorer",
        "CoreDesk Settings",
        "Settings",
        "Microsoft Store",
        "Store"
    ];

    public HomeLayout EnsureDefaultLayout(HomeLayout layout, IReadOnlyList<AppEntry> apps)
    {
        if (HasUsableUserLayout(layout, apps))
        {
            return layout;
        }

        layout.Pages = [HomePage.CreateDefault()];
        layout.Folders.Clear();
        layout.DockAppIds.Clear();

        var homeApps = PickApps(apps, HomePreferredNames).Take(20).ToList();
        var utilityApps = PickApps(apps, UtilityNames).Take(8).ToList();
        AppFolder? utilityFolder = null;
        if (utilityApps.Count > 0)
        {
            utilityFolder = new AppFolder
            {
                Name = "Utilities",
                AppIds = [.. utilityApps.Select(app => app.Id)],
                Color = "#E8F7F8FA"
            };
            layout.Folders.Add(utilityFolder);
        }

        var homeTiles = new List<HomeTile>();
        foreach (var app in homeApps.Take(13))
        {
            homeTiles.Add(new HomeTile { AppId = app.Id });
        }

        if (utilityFolder is not null)
        {
            homeTiles.Add(new HomeTile { FolderId = utilityFolder.Id });
        }

        foreach (var app in homeApps.Skip(13))
        {
            homeTiles.Add(new HomeTile { AppId = app.Id });
        }

        const int pageCapacity = 16;
        for (var index = 0; index < homeTiles.Count; index++)
        {
            var pageIndex = index / pageCapacity;
            var page = layout.Pages.FirstOrDefault(candidate => candidate.Index == pageIndex);
            if (page is null)
            {
                page = new HomePage { Index = pageIndex };
                layout.Pages.Add(page);
            }

            var position = index % pageCapacity;
            homeTiles[index].Column = position % 8;
            homeTiles[index].Row = position / 8;
            page.Tiles.Add(homeTiles[index]);
        }

        layout.DockAppIds = [.. PickApps(apps, DockNames).Take(6).Select(app => app.Id)];
        return layout;
    }

    private static bool HasUsableUserLayout(HomeLayout layout, IReadOnlyList<AppEntry> apps)
    {
        var appIds = apps.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validTiles = layout.Pages
            .SelectMany(page => page.Tiles)
            .Any(tile => !string.Equals(tile.AppId, "coredesk-settings", StringComparison.OrdinalIgnoreCase) && appIds.Contains(tile.AppId));
        var validDockItems = layout.DockAppIds.Any(appIds.Contains);
        var validFolders = layout.Folders.Any(folder => folder.AppIds.Any(appIds.Contains));
        return validTiles || validDockItems || validFolders;
    }

    private static IEnumerable<AppEntry> PickApps(IReadOnlyList<AppEntry> apps, IReadOnlyList<string> names)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var match = apps.FirstOrDefault(app => IsUsableDefaultCandidate(app) && app.DisplayName.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                ?? apps.FirstOrDefault(app => IsUsableDefaultCandidate(app) && app.DisplayName.StartsWith(name, StringComparison.CurrentCultureIgnoreCase))
                ?? apps.FirstOrDefault(app => AllowsContainsMatch(name) && IsUsableDefaultCandidate(app) && app.DisplayName.Contains(name, StringComparison.CurrentCultureIgnoreCase));

            if (match is not null && yielded.Add(match.Id))
            {
                yield return match;
            }
        }
    }

    private static bool AllowsContainsMatch(string name)
    {
        return name is "Browser" or "Store" or "Terminal" or "PowerShell";
    }

    private static bool IsUsableDefaultCandidate(AppEntry app)
    {
        string[] blockedTerms =
        [
            "Release Notes",
            "Documentation",
            "Manual",
            "Uninstall",
            "Install Manager",
            "Installer",
            "Updater",
            "Error Reporter",
            "Bug Report",
            "Language Preferences"
        ];

        return !blockedTerms.Any(term => app.DisplayName.Contains(term, StringComparison.CurrentCultureIgnoreCase));
    }
}
