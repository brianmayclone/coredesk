using System.Windows.Forms;
using System.Diagnostics;
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
        var parentPid = TryReadParentPid(args);
        var homeMode = args.Any(arg => arg.Equals("--home-mode", StringComparison.OrdinalIgnoreCase));
        using var parentMonitor = CreateParentMonitor(parentPid);
        using var form = new NativeDockForm(Services.CreateShellViewModel(), Services.Diagnostics, parentPid, homeMode);
        System.Windows.Forms.Application.Run(form);
        Services.Dispose();
    }

    private static System.Windows.Forms.Timer? CreateParentMonitor(int? parentPid)
    {
        if (parentPid is null)
        {
            Services.Diagnostics.Info("CoreDesk.Dock parent monitor disabled; no parent PID supplied.");
            return null;
        }

        var timer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };
        timer.Tick += (_, _) =>
        {
            try
            {
                using var parent = Process.GetProcessById(parentPid.Value);
                if (!parent.HasExited)
                {
                    return;
                }
            }
            catch
            {
            }

            Services.Diagnostics.Info($"CoreDesk.App parent process {parentPid.Value} is gone; exiting dock.");
            timer.Stop();
            System.Windows.Forms.Application.Exit();
        };
        timer.Start();
        Services.Diagnostics.Info($"CoreDesk.Dock parent monitor attached to PID {parentPid.Value}.");
        return timer;
    }

    private static int? TryReadParentPid(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals("--parent-pid", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[index + 1], out var pid)
                && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }
}
