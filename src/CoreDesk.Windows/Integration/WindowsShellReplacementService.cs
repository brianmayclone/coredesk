using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Windows.Integration;

public sealed class WindowsShellReplacementService(IDiagnosticsService? diagnostics = null) : IShellReplacementService
{
    private const string UserWinlogonKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string ShellValueName = "Shell";
    private const uint EVENT_MODIFY_STATE = 0x0002;

    public bool IsSessionReplacementActive { get; private set; }

    public bool IsExplorerShellRunning() => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    public bool IsConfiguredAsUserShell(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserWinlogonKeyPath, writable: false);
        if (key?.GetValue(ShellValueName) is not string configuredShell || string.IsNullOrWhiteSpace(configuredShell))
        {
            return false;
        }

        var configuredExecutable = ExtractExecutableName(configuredShell);
        var currentExecutable = Path.GetFileName(executablePath);
        return configuredExecutable.Equals(currentExecutable, StringComparison.OrdinalIgnoreCase)
            || PathsEqual(configuredExecutable, executablePath);
    }

    public void ReplaceExplorerForSession()
    {
        var explorerProcessIds = GetExplorerShellProcessIds();
        if (explorerProcessIds.Count == 0)
        {
            diagnostics?.Info("Explorer shell was not running; session shell replacement is already active.");
            IsSessionReplacementActive = true;
            return;
        }

        diagnostics?.Info($"Stopping Explorer shell for current session. Processes: {string.Join(", ", explorerProcessIds)}.");
        foreach (var processId in explorerProcessIds)
        {
            StopProcess(processId);
        }

        IsSessionReplacementActive = true;
    }

    public void RestoreExplorerForSession()
    {
        if (!IsSessionReplacementActive)
        {
            return;
        }

        IsSessionReplacementActive = false;
        if (IsExplorerShellRunning())
        {
            diagnostics?.Info("Explorer shell is already running; no restore launch required.");
            return;
        }

        diagnostics?.Info("Restarting Explorer shell after session shell replacement.");
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    public void SignalShellReady()
    {
        using var shellReadyEvent = OpenEvent(EVENT_MODIFY_STATE, false, "ShellReadyEvent");
        if (shellReadyEvent.IsInvalid)
        {
            diagnostics?.Info("ShellReadyEvent was not available in this session.");
            return;
        }

        _ = SetEvent(shellReadyEvent);
        diagnostics?.Info("ShellReadyEvent signaled.");
    }

    public void Dispose()
    {
        RestoreExplorerForSession();
    }

    private static IReadOnlySet<int> GetExplorerShellProcessIds()
    {
        var processIds = new HashSet<int>();
        AddWindowProcessId(processIds, FindWindow("Shell_TrayWnd", null));
        AddWindowProcessId(processIds, FindWindow("Progman", null));
        return processIds;
    }

    private static void StopProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            if (!process.WaitForExit(2000))
            {
                process.Kill(entireProcessTree: false);
                process.WaitForExit(4000);
            }
        }
        catch (ArgumentException)
        {
        }
    }

    private static void AddWindowProcessId(ISet<int> processIds, IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId != 0)
        {
            processIds.Add((int)processId);
        }
    }

    private static string ExtractExecutableName(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                return Path.GetFileName(trimmed[1..closingQuote]);
            }
        }

        var firstToken = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? trimmed;
        return Path.GetFileName(firstToken);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle OpenEvent(uint dwDesiredAccess, bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetEvent(SafeWaitHandle hEvent);
}
