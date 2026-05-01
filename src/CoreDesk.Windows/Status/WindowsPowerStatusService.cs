using System.Windows.Forms;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Windows.Status;

public sealed class WindowsPowerStatusService : IPowerStatusService
{
    public int? GetBatteryPercent()
    {
        var percent = SystemInformation.PowerStatus.BatteryLifePercent;
        return percent < 0 ? null : (int)Math.Round(percent * 100);
    }

    public bool IsCharging()
    {
        return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
    }
}

