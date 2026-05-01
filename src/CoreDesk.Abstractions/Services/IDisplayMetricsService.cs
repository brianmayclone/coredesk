using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IDisplayMetricsService
{
    DisplayMetrics GetPrimaryDisplayMetrics();
}
