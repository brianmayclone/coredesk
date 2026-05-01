namespace CoreDesk.Abstractions.Services;

public interface IHardwareMonitor : IDisposable
{
    event EventHandler<HardwareStateChangedEventArgs>? HardwareStateChanged;

    HardwareSnapshot Current { get; }

    void Start();
}

public sealed record HardwareSnapshot(
    bool IsTouchPresent,
    bool IsKeyboardPresent,
    bool IsMousePresent,
    int DisplayCount);

public sealed class HardwareStateChangedEventArgs(HardwareSnapshot snapshot) : EventArgs
{
    public HardwareSnapshot Snapshot { get; } = snapshot;
}

