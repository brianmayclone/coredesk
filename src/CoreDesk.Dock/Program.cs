using System.Windows.Forms;
using CoreDesk.Abstractions.Models;

namespace CoreDesk_Dock;

public static class Program
{
    public static DockComposition Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        var launchArguments = string.Join(" ", args);
        Services = new DockComposition(LaunchOptions.Parse(launchArguments));
        Services.Diagnostics.Info($"CoreDesk.Dock native host launched with args: {launchArguments}");

        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        using var form = new NativeDockForm(Services.CreateShellViewModel(), Services.Diagnostics);
        System.Windows.Forms.Application.Run(form);
        Services.Dispose();
    }
}
