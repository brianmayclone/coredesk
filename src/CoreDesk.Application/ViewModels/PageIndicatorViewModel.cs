namespace CoreDesk.Application.ViewModels;

public sealed record PageIndicatorViewModel(int Index, bool IsCurrent)
{
    public double Width => IsCurrent ? 30 : 8;

    public double Opacity => IsCurrent ? 1.0 : 0.62;
}
