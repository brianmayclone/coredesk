using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.ViewModels;

public sealed record DockItemViewModel(AppEntry App, bool IsRunning, string? WindowTitle = null, string? PreviewPath = null)
{
    public string DisplayName => App.DisplayName;

    public string? IconPath => App.IconPath;

    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewPath);
}
