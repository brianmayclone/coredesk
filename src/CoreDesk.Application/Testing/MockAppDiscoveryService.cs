using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockAppDiscoveryService : IAppDiscoveryService
{
    private static readonly IReadOnlyList<AppEntry> Apps =
    [
        new("mock-browser", "Browser", AppKind.SystemAction),
        new("mock-files", "File Explorer", AppKind.SystemAction),
        new("mock-mail", "Mail", AppKind.SystemAction),
        new("mock-calendar", "Calendar", AppKind.SystemAction),
        new("mock-photos", "Photos", AppKind.SystemAction),
        new("mock-music", "Music", AppKind.SystemAction),
        new("mock-notes", "Notes", AppKind.SystemAction),
        new("mock-store", "Store", AppKind.SystemAction),
        new("mock-settings", "Settings", AppKind.SystemAction),
        new("mock-terminal", "Terminal", AppKind.SystemAction),
        new("mock-camera", "Camera", AppKind.SystemAction),
        new("mock-maps", "Maps", AppKind.SystemAction)
    ];

    public Task<IReadOnlyList<AppEntry>> DiscoverAppsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Apps);
    }
}

