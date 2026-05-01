using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockDisplayMetricsService : IDisplayMetricsService
{
    public DisplayMetrics GetPrimaryDisplayMetrics()
    {
        return new DisplayMetrics(3840, 2160, 192, 192, 70, 39.5);
    }
}
