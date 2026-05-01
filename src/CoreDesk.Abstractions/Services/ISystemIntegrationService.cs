using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface ISystemIntegrationService : IDisposable
{
    event EventHandler<SystemIntegrationCommand>? CommandRequested;

    void Initialize();

    void SetTaskbarVisible(bool visible);

    void ShowTrayIcon();

    void HideTrayIcon();
}
