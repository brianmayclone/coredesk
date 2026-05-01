namespace CoreDesk.Abstractions.Models;

public sealed record SystemStatus(
    DateTimeOffset Now,
    int? BatteryPercent,
    bool IsCharging,
    bool IsNetworkAvailable,
    bool IsKeyboardPresent,
    ShellMode Mode);

