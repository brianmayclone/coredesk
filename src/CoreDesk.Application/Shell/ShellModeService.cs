using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Shell;

public sealed class ShellModeService(ISystemIntegrationService systemIntegration) : IShellModeService
{
    public ShellMode CurrentMode { get; private set; } = ShellMode.Touch;

    public event EventHandler<ShellMode>? ModeChanged;

    public void EnterTouchMode()
    {
        if (CurrentMode == ShellMode.Touch)
        {
            return;
        }

        CurrentMode = ShellMode.Touch;
        systemIntegration.SetTaskbarVisible(false);
        ModeChanged?.Invoke(this, CurrentMode);
    }

    public void EnterDesktopMode()
    {
        if (CurrentMode == ShellMode.Desktop)
        {
            return;
        }

        CurrentMode = ShellMode.Desktop;
        systemIntegration.SetTaskbarVisible(true);
        ModeChanged?.Invoke(this, CurrentMode);
    }
}

