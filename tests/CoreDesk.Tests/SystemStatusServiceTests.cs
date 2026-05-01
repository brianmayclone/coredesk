using CoreDesk.Abstractions.Models;
using CoreDesk.Application.SystemStatus;
using CoreDesk.Application.Testing;

namespace CoreDesk.Tests;

public sealed class SystemStatusServiceTests
{
    [Fact]
    public void GetStatus_CombinesPowerNetworkKeyboardAndMode()
    {
        var power = new MockPowerStatusService { BatteryPercent = 42, Charging = true };
        var network = new MockNetworkStatusService { NetworkAvailable = true };
        var service = new SystemStatusService(power, network);

        var status = service.GetStatus(ShellMode.Touch, isKeyboardPresent: false);

        Assert.Equal(42, status.BatteryPercent);
        Assert.True(status.IsCharging);
        Assert.True(status.IsNetworkAvailable);
        Assert.False(status.IsKeyboardPresent);
        Assert.Equal(ShellMode.Touch, status.Mode);
    }
}

