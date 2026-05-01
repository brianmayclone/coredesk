using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.ViewModels;

public sealed record HomeTileViewModel(AppEntry? App, FolderTileViewModel? Folder)
{
    public bool IsAppTile => App is not null;

    public bool IsFolderTile => Folder is not null;

    public string DisplayName => App?.DisplayName ?? Folder?.Name ?? string.Empty;

    public string BackgroundKey => DisplayName;

    public string? IconPath => App?.IconPath;

    public int AppCount => Folder?.AppCount ?? 0;
}
