using CoreDesk.Abstractions.Models;
using CoreDesk.Application.ViewModels;

namespace CoreDesk.Application.AppClassification;

public static class AppCategoryClassifier
{
    private static readonly CategoryRule[] Rules =
    [
        new("Productivity", "\uE8A5", ["word", "excel", "powerpoint", "onenote", "outlook", "office", "publisher", "sticky notes", "notepad", "notes", "calendar", "mail"]),
        new("Internet", "\uE774", ["edge", "chrome", "browser", "firefox", "samsung browser", "remote desktop", "web", "teams"]),
        new("Creative", "\uE790", ["photos", "paint", "camera", "media", "music", "video", "clipchamp", "snipping"]),
        new("Development", "\uE943", ["visual studio", "code", "git", "github", "python", "node", "npm", "cmake", "msys2", "terminal", "powershell", "command prompt", "developer"]),
        new("System", "\uE713", ["settings", "control panel", "task manager", "event viewer", "services", "registry", "device", "disk", "monitor", "management", "configuration", "diagnostic"]),
        new("Utilities", "\uE71D", ["calculator", "maps", "magnify", "narrator", "keyboard", "quick share", "run", "tools", "utility", "access"])
    ];

    public static DrawerCategoryViewModel[] BuildCategories(IReadOnlyList<AppEntry> apps)
    {
        var buckets = Rules.ToDictionary(rule => rule.Name, _ => new List<AppEntry>(), StringComparer.OrdinalIgnoreCase);
        var other = new List<AppEntry>();

        foreach (var app in apps.OrderBy(app => app.DisplayName))
        {
            var rule = Rules.FirstOrDefault(rule => rule.Matches(app));
            if (rule is null)
            {
                other.Add(app);
            }
            else
            {
                buckets[rule.Name].Add(app);
            }
        }

        var categories = Rules
            .Select(rule => new DrawerCategoryViewModel(rule.Name, rule.Glyph, buckets[rule.Name]))
            .Where(category => category.Count > 0)
            .ToList();

        if (other.Count > 0)
        {
            categories.Add(new DrawerCategoryViewModel("Other", "\uECAA", other));
        }

        return [.. categories];
    }

    private sealed record CategoryRule(string Name, string Glyph, string[] Keywords)
    {
        public bool Matches(AppEntry app)
        {
            return Keywords.Any(keyword => app.DisplayName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        }
    }
}
