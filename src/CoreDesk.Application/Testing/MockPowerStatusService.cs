using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockPowerStatusService : IPowerStatusService
{
    public int? BatteryPercent { get; set; } = 86;

    public bool Charging { get; set; } = true;

    public int? GetBatteryPercent() => BatteryPercent;

    public bool IsCharging() => Charging;
}

