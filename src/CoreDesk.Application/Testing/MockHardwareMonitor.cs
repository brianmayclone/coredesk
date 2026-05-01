using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Testing;

public sealed class MockHardwareMonitor : IHardwareMonitor
{
    public event EventHandler<HardwareStateChangedEventArgs>? HardwareStateChanged;

    public HardwareSnapshot Current { get; private set; } = new(true, false, false, 1);

    public void Start()
    {
    }

    public void SetKeyboardPresent(bool present)
    {
        Current = Current with { IsKeyboardPresent = present };
        HardwareStateChanged?.Invoke(this, new HardwareStateChangedEventArgs(Current));
    }

    public void Dispose()
    {
    }
}

