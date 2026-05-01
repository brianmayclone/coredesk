using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.Dock;

public static class DockRunningAppMatcher
{
    public static IReadOnlySet<string> MatchRunningAppIds(
        IReadOnlyList<RunningAppEntry> runningApps,
        IReadOnlyList<AppEntry> allApps,
        IReadOnlyList<AppEntry> pinnedApps)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var running in runningApps)
        {
            var pinnedMatch = pinnedApps.FirstOrDefault(app => IsRunningMatch(app, running));
            if (pinnedMatch is not null)
            {
                ids.Add(pinnedMatch.Id);
                continue;
            }

            var appMatch = allApps.FirstOrDefault(app => IsRunningMatch(app, running));
            if (appMatch is not null)
            {
                ids.Add(appMatch.Id);
            }
        }

        return ids;
    }

    public static bool IsRunningMatch(AppEntry app, RunningAppEntry running)
    {
        if (!string.IsNullOrWhiteSpace(app.AppUserModelId)
            && app.AppUserModelId.Equals(running.AppUserModelId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MatchesExecutableName(app.ExecutablePath, running.ProcessName)
            || MatchesExecutableName(app.ExecutablePath, running.ExecutablePath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(running.WindowTitle)
            && running.WindowTitle.Contains(app.DisplayName, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        return app.DisplayName.Contains(running.ProcessName, StringComparison.CurrentCultureIgnoreCase)
            || running.ProcessName.Contains(app.DisplayName, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool MatchesExecutableName(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftName = Path.GetFileNameWithoutExtension(left);
        var rightName = Path.GetFileNameWithoutExtension(right);
        return !string.IsNullOrWhiteSpace(leftName)
            && leftName.Equals(rightName, StringComparison.OrdinalIgnoreCase);
    }
}
