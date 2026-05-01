using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockWallpaperService : IWallpaperService
{
    public string? GetCurrentWallpaperPath()
    {
        return null;
    }
}
