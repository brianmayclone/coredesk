using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.ViewModels;

public sealed record DrawerCategoryViewModel(string Name, string Glyph, IReadOnlyList<AppEntry> Apps)
{
    public int Count => Apps.Count;
}
