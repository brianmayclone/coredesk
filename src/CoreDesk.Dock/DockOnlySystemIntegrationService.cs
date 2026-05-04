using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk_Dock;

public sealed class DockOnlySystemIntegrationService(IDiagnosticsService diagnostics) : ISystemIntegrationService
{
    public event EventHandler<SystemIntegrationCommand>? CommandRequested;

    public void Initialize()
    {
        diagnostics.Info("Dock-only system integration initialized.");
    }

    public void SetTaskbarVisible(bool visible)
    {
        diagnostics.Info($"Dock-only taskbar request ignored: {visible}.");
    }

    public void ReserveTopWorkArea(IntPtr ownerWindowHandle, int reservedPixels)
    {
        diagnostics.Info($"Dock-only work area request ignored: {reservedPixels}.");
    }

    public void RestoreWorkArea()
    {
    }

    public void ShowTrayIcon()
    {
    }

    public void HideTrayIcon()
    {
    }

    public void Dispose()
    {
    }

    public void Request(SystemIntegrationCommand command)
    {
        CommandRequested?.Invoke(this, command);
    }
}
