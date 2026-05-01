using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface IShellModeService
{
    ShellMode CurrentMode { get; }

    event EventHandler<ShellMode>? ModeChanged;

    void EnterTouchMode();

    void EnterDesktopMode();
}

