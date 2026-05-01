using CoreDesk.Abstractions.Models;
using CoreDesk.Application.Search;

namespace CoreDesk.Tests;

public sealed class AppSearchServiceTests
{
    private readonly AppSearchService _service = new();

    [Fact]
    public void Search_ReturnsAlphabeticalAppsForEmptyQuery()
    {
        var result = _service.Search(CreateApps(), "");

        Assert.Equal(["Calendar", "File Explorer", "Photos"], result.Select(app => app.DisplayName));
    }

    [Fact]
    public void Search_PrioritizesPrefixMatch()
    {
        var result = _service.Search(CreateApps(), "pho");

        Assert.Equal("Photos", result[0].DisplayName);
    }

    [Fact]
    public void Search_SupportsSimpleFuzzySubsequence()
    {
        var result = _service.Search(CreateApps(), "fe");

        Assert.Contains(result, app => app.DisplayName == "File Explorer");
    }

    private static IReadOnlyList<AppEntry> CreateApps()
    {
        return
        [
            new("photos", "Photos", AppKind.Win32),
            new("files", "File Explorer", AppKind.Win32),
            new("calendar", "Calendar", AppKind.Win32)
        ];
    }
}

