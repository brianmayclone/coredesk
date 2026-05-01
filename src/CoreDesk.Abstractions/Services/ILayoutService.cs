using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface ILayoutService
{
    HomeLayout AddAppToHome(HomeLayout layout, string appId, int pageIndex, int column, int row);

    HomeLayout AddAppToDock(HomeLayout layout, string appId, int? targetIndex = null);

    HomeLayout RemoveAppFromDock(HomeLayout layout, string appId);

    HomeLayout CreateFolder(HomeLayout layout, string folderName, string firstAppId, string secondAppId);

    HomeLayout AddAppToFolder(HomeLayout layout, string folderId, string appId);

    HomeLayout RemoveAppFromFolder(HomeLayout layout, string folderId, string appId);
}

