using CoreDesk.Abstractions.Services;
using Windows.Devices.Input;

namespace CoreDesk.Windows.Hardware;

public sealed class PollingHardwareMonitor : IHardwareMonitor
{
    private readonly System.Threading.Timer _timer;

    public PollingHardwareMonitor()
    {
        Current = Capture();
        _timer = new System.Threading.Timer(_ => Poll(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<HardwareStateChangedEventArgs>? HardwareStateChanged;

    public HardwareSnapshot Current { get; private set; }

    public void Start()
    {
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void Poll()
    {
        var snapshot = Capture();
        if (snapshot != Current)
        {
            Current = snapshot;
            HardwareStateChanged?.Invoke(this, new HardwareStateChangedEventArgs(snapshot));
        }
    }

    private static HardwareSnapshot Capture()
    {
        var touch = new TouchCapabilities();
        var keyboard = new KeyboardCapabilities();
        var mouse = new MouseCapabilities();

        return new HardwareSnapshot(
            touch.TouchPresent != 0,
            keyboard.KeyboardPresent != 0,
            mouse.MousePresent != 0,
            System.Windows.Forms.Screen.AllScreens.Length);
    }
}
