using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IAppSearchService
{
    IReadOnlyList<AppEntry> Search(IEnumerable<AppEntry> apps, string query);
}

