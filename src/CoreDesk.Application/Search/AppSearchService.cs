using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Search;

public sealed class AppSearchService : IAppSearchService
{
    public IReadOnlyList<AppEntry> Search(IEnumerable<AppEntry> apps, string query)
    {
        var normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [.. apps.OrderBy(app => app.DisplayName)];
        }

        return [.. apps
            .Select(app => new { App = app, Score = Score(Normalize(app.DisplayName), normalizedQuery) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.App.DisplayName)
            .Select(item => item.App)];
    }

    private static int Score(string candidate, string query)
    {
        if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (candidate.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        return IsSubsequence(candidate, query) ? 30 : 0;
    }

    private static bool IsSubsequence(string candidate, string query)
    {
        var queryIndex = 0;
        foreach (var character in candidate)
        {
            if (queryIndex < query.Length && character == query[queryIndex])
            {
                queryIndex++;
            }
        }

        return queryIndex == query.Length;
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}

