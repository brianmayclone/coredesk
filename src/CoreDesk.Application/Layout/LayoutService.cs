using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Layout;

public sealed class LayoutService : ILayoutService
{
    public HomeLayout AddAppToHome(HomeLayout layout, string appId, int pageIndex, int column, int row)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var page = GetOrCreatePage(layout, pageIndex);
        page.Tiles.RemoveAll(tile => tile.Column == column && tile.Row == row);
        RemoveAppFromHome(layout, appId);
        page.Tiles.Add(new HomeTile { AppId = appId, Column = column, Row = row });
        return layout;
    }

    public HomeLayout AddAppToDock(HomeLayout layout, string appId, int? targetIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        layout.DockAppIds.Remove(appId);
        var index = Math.Clamp(targetIndex ?? layout.DockAppIds.Count, 0, layout.DockAppIds.Count);
        layout.DockAppIds.Insert(index, appId);
        return layout;
    }

    public HomeLayout RemoveAppFromDock(HomeLayout layout, string appId)
    {
        layout.DockAppIds.Remove(appId);
        return layout;
    }

    public HomeLayout CreateFolder(HomeLayout layout, string folderName, string firstAppId, string secondAppId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondAppId);
        if (firstAppId.Equals(secondAppId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A folder needs two different apps.");
        }

        var folder = new AppFolder
        {
            Name = string.IsNullOrWhiteSpace(folderName) ? "Folder" : folderName,
            AppIds = [firstAppId, secondAppId]
        };

        RemoveAppFromHome(layout, firstAppId);
        RemoveAppFromHome(layout, secondAppId);
        layout.DockAppIds.Remove(firstAppId);
        layout.DockAppIds.Remove(secondAppId);
        layout.Folders.Add(folder);
        return layout;
    }

    public HomeLayout AddAppToFolder(HomeLayout layout, string folderId, string appId)
    {
        var folder = FindFolder(layout, folderId);
        RemoveAppFromHome(layout, appId);
        layout.DockAppIds.Remove(appId);
        if (!folder.AppIds.Contains(appId, StringComparer.OrdinalIgnoreCase))
        {
            folder.AppIds.Add(appId);
        }

        return layout;
    }

    public HomeLayout RemoveAppFromFolder(HomeLayout layout, string folderId, string appId)
    {
        var folder = FindFolder(layout, folderId);
        folder.AppIds.Remove(appId);
        if (folder.AppIds.Count == 0)
        {
            layout.Folders.Remove(folder);
        }

        return layout;
    }

    private static HomePage GetOrCreatePage(HomeLayout layout, int pageIndex)
    {
        var page = layout.Pages.FirstOrDefault(item => item.Index == pageIndex);
        if (page is not null)
        {
            return page;
        }

        page = new HomePage { Index = pageIndex };
        layout.Pages.Add(page);
        layout.Pages.Sort((left, right) => left.Index.CompareTo(right.Index));
        return page;
    }

    private static void RemoveAppFromHome(HomeLayout layout, string appId)
    {
        foreach (var page in layout.Pages)
        {
            page.Tiles.RemoveAll(tile => tile.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static AppFolder FindFolder(HomeLayout layout, string folderId)
    {
        return layout.Folders.FirstOrDefault(folder => folder.Id.Equals(folderId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Folder '{folderId}' was not found.");
    }
}

