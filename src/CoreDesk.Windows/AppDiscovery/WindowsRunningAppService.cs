using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace CoreDesk.Windows.AppDiscovery;

public sealed class WindowsRunningAppService : IRunningAppService
{
    private const int SW_RESTORE = 9;
    private const int PW_RENDERFULLCONTENT = 2;
    private static readonly string PreviewDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoreDesk",
        "WindowPreviews");

    public Task<IReadOnlyList<RunningAppEntry>> GetRunningAppsAsync(CancellationToken cancellationToken = default)
    {
        var apps = new Dictionary<uint, RunningAppEntry>();
        EnumWindows((handle, lParam) =>
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
                var previewPath = TryCapturePreview(handle, processId);
                apps[processId] = new RunningAppEntry(process.ProcessName, executable, title, PreviewPath: previewPath);
            }
            catch
            {
                // Some system processes deny inspection; skip them.
            }

            return true;
        }, IntPtr.Zero);

        return Task.FromResult<IReadOnlyList<RunningAppEntry>>([.. apps.Values.OrderBy(app => app.ProcessName)]);
    }

    public Task<bool> TryActivateAsync(AppEntry app, CancellationToken cancellationToken = default)
    {
        var activated = false;
        EnumWindows((handle, lParam) =>
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
            if (processId == 0)
            {
                return true;
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                var running = new RunningAppEntry(process.ProcessName, GetExecutablePath(process), GetTitle(handle));
                if (!Matches(app, running))
                {
                    return true;
                }

                ShowWindow(handle, SW_RESTORE);
                SetForegroundWindow(handle);
                activated = true;
                return false;
            }
            catch
            {
                return true;
            }
        }, IntPtr.Zero);

        return Task.FromResult(activated);
    }

    private static bool Matches(AppEntry app, RunningAppEntry running)
    {
        if (!string.IsNullOrWhiteSpace(app.AppUserModelId)
            && app.AppUserModelId.Equals(running.AppUserModelId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MatchesExecutableName(app.ExecutablePath, running.ProcessName)
            || MatchesExecutableName(app.ExecutablePath, running.ExecutablePath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(running.WindowTitle)
            && running.WindowTitle.Contains(app.DisplayName, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        return app.DisplayName.Contains(running.ProcessName, StringComparison.CurrentCultureIgnoreCase)
            || running.ProcessName.Contains(app.DisplayName, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool MatchesExecutableName(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftName = Path.GetFileNameWithoutExtension(left);
        var rightName = Path.GetFileNameWithoutExtension(right);
        return !string.IsNullOrWhiteSpace(leftName)
            && leftName.Equals(rightName, StringComparison.OrdinalIgnoreCase);
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

    private static string? TryCapturePreview(IntPtr handle, uint processId)
    {
        if (!GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var sourceWidth = rect.Right - rect.Left;
        var sourceHeight = rect.Bottom - rect.Top;
        if (sourceWidth < 160 || sourceHeight < 120)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(PreviewDirectory);
            var path = Path.Combine(PreviewDirectory, $"{processId}.png");
            using var source = new Bitmap(sourceWidth, sourceHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source))
            {
                var hdc = graphics.GetHdc();
                var rendered = PrintWindow(handle, hdc, PW_RENDERFULLCONTENT);
                graphics.ReleaseHdc(hdc);
                if (!rendered)
                {
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(sourceWidth, sourceHeight));
                }
            }

            var targetWidth = 720;
            var targetHeight = Math.Clamp((int)Math.Round(sourceHeight * (targetWidth / (double)sourceWidth)), 320, 520);
            using var target = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(target))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height));
            }

            target.Save(path, ImageFormat.Png);
            return path;
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
