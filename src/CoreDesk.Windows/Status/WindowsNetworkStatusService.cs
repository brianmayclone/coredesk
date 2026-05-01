using System.Net.NetworkInformation;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Windows.Status;

public sealed class WindowsNetworkStatusService : INetworkStatusService
{
    public bool IsNetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();
}

