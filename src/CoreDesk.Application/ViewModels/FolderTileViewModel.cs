using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.ViewModels;

public sealed record FolderTileViewModel(AppFolder Folder, int AppCount, IReadOnlyList<AppEntry>? PreviewApps = null)
{
    public string Name => Folder.Name;
}
