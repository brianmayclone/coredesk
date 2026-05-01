using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.ViewModels;

public sealed record HomeWidgetViewModel(HomeWidget Widget, string PrimaryText, string SecondaryText)
{
    public string Title => Widget.Title;
}

