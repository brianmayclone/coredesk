using CoreDesk.Abstractions.Models;
using CoreDesk.Application.Layout;

namespace CoreDesk.Tests;

public sealed class LayoutServiceTests
{
    private readonly LayoutService _service = new();

    [Fact]
    public void AddAppToHome_CreatesPageAndPlacesTile()
    {
        var layout = new HomeLayout();

        _service.AddAppToHome(layout, "app-one", 2, 3, 1);

        var page = Assert.Single(layout.Pages, page => page.Index == 2);
        var tile = Assert.Single(page.Tiles);
        Assert.Equal("app-one", tile.AppId);
        Assert.Equal(3, tile.Column);
        Assert.Equal(1, tile.Row);
    }

    [Fact]
    public void AddAppToDock_ReordersExistingAppWithoutDuplicates()
    {
        var layout = new HomeLayout { DockAppIds = ["one", "two", "three"] };

        _service.AddAppToDock(layout, "three", 0);

        Assert.Equal(["three", "one", "two"], layout.DockAppIds);
    }

    [Fact]
    public void CreateFolder_RemovesAppsFromHomeAndDock()
    {
        var layout = new HomeLayout { DockAppIds = ["one"] };
        _service.AddAppToHome(layout, "one", 0, 0, 0);
        _service.AddAppToHome(layout, "two", 0, 1, 0);

        _service.CreateFolder(layout, "Work", "one", "two");

        var folder = Assert.Single(layout.Folders);
        Assert.Equal("Work", folder.Name);
        Assert.Equal(["one", "two"], folder.AppIds);
        Assert.Empty(layout.Pages.SelectMany(page => page.Tiles));
        Assert.Empty(layout.DockAppIds);
    }

    [Fact]
    public void AddAppToFolder_DoesNotDuplicateApps()
    {
        var layout = new HomeLayout();
        _service.CreateFolder(layout, "Work", "one", "two");
        var folder = layout.Folders[0];

        _service.AddAppToFolder(layout, folder.Id, "two");

        Assert.Equal(["one", "two"], folder.AppIds);
    }
}
