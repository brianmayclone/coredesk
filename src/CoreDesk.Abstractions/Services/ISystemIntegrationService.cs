using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface ISystemIntegrationService : IDisposable
{
    event EventHandler<SystemIntegrationCommand>? CommandRequested;

    void Initialize();

    void SetTaskbarVisible(bool visible);

    void ReserveTopWorkArea(IntPtr ownerWindowHandle, int reservedPixels);

    void RestoreWorkArea();

    void ShowTrayIcon();

    void HideTrayIcon();

    int GetVolumePercent();

    void SetVolumePercent(int percent);

    bool IsMuted();

    void SetMuted(bool muted);

    int? GetBrightnessPercent();

    void SetBrightnessPercent(int percent);

    void LockScreen();

    void OpenSystemPanel(string panelUri);
}
