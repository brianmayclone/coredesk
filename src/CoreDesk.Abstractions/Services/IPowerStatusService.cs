namespace CoreDesk.Abstractions.Services;

public interface IPowerStatusService
{
    int? GetBatteryPercent();

    bool IsCharging();
}

