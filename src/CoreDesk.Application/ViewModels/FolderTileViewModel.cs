using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.ViewModels;

public sealed record FolderTileViewModel(AppFolder Folder, int AppCount)
{
    public string Name => Folder.Name;
}
