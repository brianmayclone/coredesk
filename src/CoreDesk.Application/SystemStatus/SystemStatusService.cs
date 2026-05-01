using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.SystemStatus;

public sealed class SystemStatusService(
    IPowerStatusService powerStatus,
    INetworkStatusService networkStatus) : ISystemStatusService
{
    public CoreDesk.Abstractions.Models.SystemStatus GetStatus(ShellMode mode, bool isKeyboardPresent)
    {
        return new CoreDesk.Abstractions.Models.SystemStatus(
            DateTimeOffset.Now,
            powerStatus.GetBatteryPercent(),
            powerStatus.IsCharging(),
            networkStatus.IsNetworkAvailable(),
            isKeyboardPresent,
            mode);
    }
}
