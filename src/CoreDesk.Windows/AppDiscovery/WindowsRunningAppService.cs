using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CoreDesk.Windows.AppDiscovery;

public sealed class WindowsRunningAppService : IRunningAppService
{
    public Task<IReadOnlyList<RunningAppEntry>> GetRunningAppsAsync(CancellationToken cancellationToken = default)
    {
        var apps = new Dictionary<uint, RunningAppEntry>();
        EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0)
            {
                return true;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || apps.ContainsKey(processId))
            {
                return true;
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (process.ProcessName.Equals("CoreDesk.App", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var title = GetTitle(handle);
                var executable = GetExecutablePath(process);
                apps[processId] = new RunningAppEntry(process.ProcessName, executable, title);
            }
            catch
            {
                // Some system processes deny inspection; skip them.
            }

            return true;
        }, IntPtr.Zero);

        return Task.FromResult<IReadOnlyList<RunningAppEntry>>([.. apps.Values.OrderBy(app => app.ProcessName)]);
    }

    private static string GetTitle(IntPtr handle)
    {
        var builder = new StringBuilder(GetWindowTextLength(handle) + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string? GetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
