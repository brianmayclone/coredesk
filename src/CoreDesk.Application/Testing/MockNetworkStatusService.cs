using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockNetworkStatusService : INetworkStatusService
{
    public bool NetworkAvailable { get; set; } = true;

    public bool IsNetworkAvailable() => NetworkAvailable;
}
