using CoreDesk.Abstractions.Models;

namespace CoreDesk.Abstractions.Services;

public interface ISystemStatusService
{
    SystemStatus GetStatus(ShellMode mode, bool isKeyboardPresent);
}

