using CoreDesk.Abstractions.Services;
using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.Testing;

public sealed class MockSystemIntegrationService(IDiagnosticsService diagnostics) : ISystemIntegrationService
{
    public bool IsTaskbarVisible { get; private set; } = true;

    public bool IsTrayVisible { get; private set; }

    public int ReservedTopWorkAreaPixels { get; private set; }

    public int VolumePercent { get; private set; } = 50;

    public bool Muted { get; private set; }

    public int BrightnessPercent { get; private set; } = 70;

    public string? LastOpenedPanel { get; private set; }

    public bool WasLocked { get; private set; }

    public event EventHandler<SystemIntegrationCommand>? CommandRequested;

    public void Initialize()
    {
        diagnostics.Info("Mock system integration initialized.");
    }

    public void SetTaskbarVisible(bool visible)
    {
        IsTaskbarVisible = visible;
        diagnostics.Info($"Mock taskbar visible: {visible}");
    }

    public void ReserveTopWorkArea(IntPtr ownerWindowHandle, int reservedPixels)
    {
        ReservedTopWorkAreaPixels = Math.Max(0, reservedPixels);
        diagnostics.Info($"Mock top work area reserved: {ReservedTopWorkAreaPixels}");
    }

    public void RestoreWorkArea()
    {
        ReservedTopWorkAreaPixels = 0;
        diagnostics.Info("Mock work area restored.");
    }

    public void ShowTrayIcon()
    {
        IsTrayVisible = true;
        diagnostics.Info("Mock tray shown.");
    }

    public void HideTrayIcon()
    {
        IsTrayVisible = false;
        diagnostics.Info("Mock tray hidden.");
    }

    public int GetVolumePercent() => VolumePercent;

    public void SetVolumePercent(int percent)
    {
        VolumePercent = Math.Clamp(percent, 0, 100);
        diagnostics.Info($"Mock volume set: {VolumePercent}");
    }

    public bool IsMuted() => Muted;

    public void SetMuted(bool muted)
    {
        Muted = muted;
        diagnostics.Info($"Mock muted: {Muted}");
    }

    public int? GetBrightnessPercent() => BrightnessPercent;

    public void SetBrightnessPercent(int percent)
    {
        BrightnessPercent = Math.Clamp(percent, 0, 100);
        diagnostics.Info($"Mock brightness set: {BrightnessPercent}");
    }

    public void LockScreen()
    {
        WasLocked = true;
        diagnostics.Info("Mock lock screen requested.");
    }

    public void OpenSystemPanel(string panelUri)
    {
        LastOpenedPanel = panelUri;
        diagnostics.Info($"Mock system panel opened: {panelUri}");
    }

    public void Dispose()
    {
        IsTaskbarVisible = true;
        RestoreWorkArea();
    }

    public void Request(SystemIntegrationCommand command)
    {
        diagnostics.Info($"Mock tray command requested: {command}");
        CommandRequested?.Invoke(this, command);
    }
}
