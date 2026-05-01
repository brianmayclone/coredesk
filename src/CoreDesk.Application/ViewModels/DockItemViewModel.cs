using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.ViewModels;

public sealed record DockItemViewModel(AppEntry App, bool IsRunning)
{
    public string DisplayName => App.DisplayName;

    public string? IconPath => App.IconPath;
}
