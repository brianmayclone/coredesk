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

    public int GetVolumePercent() => 50;

    public void SetVolumePercent(int percent)
    {
        diagnostics.Info($"Dock-only volume request ignored: {percent}.");
    }

    public bool IsMuted() => false;

    public void SetMuted(bool muted)
    {
        diagnostics.Info($"Dock-only mute request ignored: {muted}.");
    }

    public int? GetBrightnessPercent() => null;

    public void SetBrightnessPercent(int percent)
    {
        diagnostics.Info($"Dock-only brightness request ignored: {percent}.");
    }

    public void LockScreen()
    {
        diagnostics.Info("Dock-only lock request ignored.");
    }

    public void OpenSystemPanel(string panelUri)
    {
        diagnostics.Info($"Dock-only system panel request ignored: {panelUri}.");
    }

    public void Dispose()
    {
    }

    public void Request(SystemIntegrationCommand command)
    {
        CommandRequested?.Invoke(this, command);
    }
}
