using CoreDesk.Abstractions.Services;
using CoreDesk.Abstractions.Models;

namespace CoreDesk.Application.Testing;

public sealed class MockSystemIntegrationService(IDiagnosticsService diagnostics) : ISystemIntegrationService
{
    public bool IsTaskbarVisible { get; private set; } = true;

    public bool IsTrayVisible { get; private set; }

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

    public void Dispose()
    {
        IsTaskbarVisible = true;
    }

    public void Request(SystemIntegrationCommand command)
    {
        diagnostics.Info($"Mock tray command requested: {command}");
        CommandRequested?.Invoke(this, command);
    }
}
