using CoreDesk.Abstractions.Models;
using CoreDesk.Application.Layout;

namespace CoreDesk.Tests;

public sealed class DefaultLayoutBuilderTests
{
    [Fact]
    public void EnsureDefaultLayout_SeedsWindowsAppsUtilitiesFolderAndDock()
    {
        var builder = new DefaultLayoutBuilder();
        var layout = new HomeLayout();
        var apps = new[]
        {
            new AppEntry("edge", "Microsoft Edge", AppKind.Win32),
            new AppEntry("explorer", "File Explorer", AppKind.Win32),
            new AppEntry("settings", "Settings", AppKind.SystemAction),
            new AppEntry("store", "Microsoft Store", AppKind.Win32),
            new AppEntry("notepad", "Notepad", AppKind.Win32),
            new AppEntry("calc", "Calculator", AppKind.Win32),
            new AppEntry("terminal", "Terminal", AppKind.Win32)
        };

        builder.EnsureDefaultLayout(layout, apps);

        Assert.Contains(layout.Pages.SelectMany(page => page.Tiles), tile => tile.AppId == "edge");
        Assert.Contains(layout.DockAppIds, id => id == "explorer");
        var utilities = Assert.Single(layout.Folders, folder => folder.Name == "Utilities");
        Assert.Contains("notepad", utilities.AppIds);
        Assert.Contains("calc", utilities.AppIds);
    }

    [Fact]
    public void EnsureDefaultLayout_DoesNotOverwriteExistingUserLayout()
    {
        var builder = new DefaultLayoutBuilder();
        var layout = new HomeLayout
        {
            DockAppIds = ["custom"]
        };

        builder.EnsureDefaultLayout(layout, [new AppEntry("custom", "Custom App", AppKind.Win32)]);

        Assert.Equal(["custom"], layout.DockAppIds);
    }
}
