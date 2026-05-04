using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.ViewModels;

public sealed class HomeWidgetViewModel(HomeWidget widget, string primaryText, string secondaryText)
{
    private const double CellWidth = 181;
    private const double CellHeight = 132;
    private const double CellGap = 18;

    public HomeWidget Widget { get; } = widget;

    public string PrimaryText { get; } = primaryText;

    public string SecondaryText { get; } = secondaryText;

    public string Title => Widget.Title;

    public double Left => Widget.Column * (CellWidth + CellGap);

    public double Top => Widget.Row * (CellHeight + CellGap);

    public double Width => (Widget.ColumnSpan * CellWidth) + (Math.Max(0, Widget.ColumnSpan - 1) * CellGap);

    public double Height => (Widget.RowSpan * CellHeight) + (Math.Max(0, Widget.RowSpan - 1) * CellGap);

    public double Opacity { get; init; } = 1;
}
