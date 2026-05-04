using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Windows.Integration;

public sealed class WindowsSystemIntegrationService : ISystemIntegrationService
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SPI_SETWORKAREA = 0x002F;
    private const int SPI_GETWORKAREA = 0x0030;
    private const int SPIF_SENDCHANGE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private NotifyIcon? _trayIcon;
    private readonly IDiagnosticsService? diagnostics;
    private bool _disposed;
    private bool _taskbarSuppressed;
    private NativeRect? _originalWorkArea;
    private readonly object _taskbarLock = new();
    private readonly Dictionary<IntPtr, NativeRect> _taskbarPositions = [];
    private readonly System.Threading.Timer _taskbarEnforcer;

    public WindowsSystemIntegrationService(IDiagnosticsService? diagnostics = null)
    {
        this.diagnostics = diagnostics;
        _taskbarEnforcer = new System.Threading.Timer(_ =>
        {
            if (_taskbarSuppressed)
            {
                SetTaskbarVisible(false);
            }
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<SystemIntegrationCommand>? CommandRequested;

    public void Initialize()
    {
        ShowTrayIcon();
    }

    public void SetTaskbarVisible(bool visible)
    {
        lock (_taskbarLock)
        {
            _taskbarSuppressed = !visible;
            _taskbarEnforcer.Change(
                visible ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(450),
                visible ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(450));

            var taskbarWindows = EnumerateTaskbarWindows();
            diagnostics?.Info($"Windows taskbar visibility requested: {visible}; windows found: {taskbarWindows.Count}.");
            foreach (var taskbarHandle in taskbarWindows)
            {
                if (visible)
                {
                    RestoreTaskbar(taskbarHandle);
                }
                else
                {
                    HideTaskbar(taskbarHandle);
                }
            }
        }
    }

    public void ReserveTopWorkArea(IntPtr ownerWindowHandle, int reservedPixels)
    {
        var topInset = Math.Max(0, reservedPixels);
        if (!SystemParametersInfoGetWorkArea(SPI_GETWORKAREA, 0, out var currentWorkArea, 0))
        {
            diagnostics?.Info("Failed to read current Windows work area.");
            return;
        }

        _originalWorkArea ??= currentWorkArea;
        var reservedWorkArea = new NativeRect
        {
            Left = 0,
            Top = topInset,
            Right = GetSystemMetrics(SM_CXSCREEN),
            Bottom = GetSystemMetrics(SM_CYSCREEN)
        };

        if (SystemParametersInfoSetWorkArea(SPI_SETWORKAREA, 0, ref reservedWorkArea, SPIF_SENDCHANGE))
        {
            diagnostics?.Info($"Reserved top work area: {topInset}px.");
        }
        else
        {
            diagnostics?.Info("Failed to reserve Windows work area.");
        }
    }

    public void RestoreWorkArea()
    {
        if (_originalWorkArea is not { } original)
        {
            return;
        }

        var restored = original;
        if (SystemParametersInfoSetWorkArea(SPI_SETWORKAREA, 0, ref restored, SPIF_SENDCHANGE))
        {
            _originalWorkArea = null;
            diagnostics?.Info("Restored Windows work area.");
        }
        else
        {
            diagnostics?.Info("Failed to restore Windows work area.");
        }
    }

    public void ShowTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;
            return;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open CoreDesk", null, (_, _) => Request(SystemIntegrationCommand.OpenShell));
        menu.Items.Add("Touch Mode", null, (_, _) => Request(SystemIntegrationCommand.EnterTouchMode));
        menu.Items.Add("Desktop Mode", null, (_, _) => Request(SystemIntegrationCommand.EnterDesktopMode));
        menu.Items.Add("Settings", null, (_, _) => Request(SystemIntegrationCommand.OpenSettings));
        menu.Items.Add("Safe Mode", null, (_, _) => Request(SystemIntegrationCommand.EnterSafeMode));
        menu.Items.Add("Exit", null, (_, _) => Request(SystemIntegrationCommand.Exit));

        _trayIcon = new NotifyIcon
        {
            Text = "CoreDesk",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    public void HideTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _taskbarEnforcer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        SetTaskbarVisible(true);
        RestoreWorkArea();
        _taskbarEnforcer.Dispose();
        _trayIcon?.Dispose();
    }

    private void Request(SystemIntegrationCommand command)
    {
        CommandRequested?.Invoke(this, command);
    }

    private void HideTaskbar(IntPtr taskbarHandle)
    {
        if (!_taskbarPositions.ContainsKey(taskbarHandle) && GetWindowRect(taskbarHandle, out var rect))
        {
            _taskbarPositions[taskbarHandle] = rect;
            diagnostics?.Info($"Stored taskbar {taskbarHandle} rect: {rect.Left},{rect.Top},{rect.Right},{rect.Bottom}.");
        }

        EnableWindow(taskbarHandle, false);
        ShowWindow(taskbarHandle, SW_HIDE);

        if (_taskbarPositions.TryGetValue(taskbarHandle, out var original))
        {
            var width = Math.Max(1, original.Right - original.Left);
            var height = Math.Max(1, original.Bottom - original.Top);
            _ = SetWindowPos(
                taskbarHandle,
                IntPtr.Zero,
                original.Left,
                GetSystemMetrics(SM_CYSCREEN) + 80,
                width,
                height,
                SWP_NOZORDER | SWP_NOACTIVATE);
            diagnostics?.Info($"Moved taskbar {taskbarHandle} offscreen and hid it.");
        }
    }

    private void RestoreTaskbar(IntPtr taskbarHandle)
    {
        EnableWindow(taskbarHandle, true);
        if (_taskbarPositions.TryGetValue(taskbarHandle, out var original))
        {
            _ = SetWindowPos(
                taskbarHandle,
                IntPtr.Zero,
                original.Left,
                original.Top,
                Math.Max(1, original.Right - original.Left),
                Math.Max(1, original.Bottom - original.Top),
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        ShowWindow(taskbarHandle, SW_SHOW);
        diagnostics?.Info($"Restored taskbar {taskbarHandle}.");
    }

    private static IReadOnlyList<IntPtr> EnumerateTaskbarWindows()
    {
        var handles = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            var className = GetClassName(handle);
            if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                handles.Add(handle);
            }

            return true;
        }, IntPtr.Zero);

        return handles;
    }

    private static string GetClassName(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        var length = GetClassName(handle, builder, builder.Capacity);
        return length <= 0 ? string.Empty : builder.ToString(0, length);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfoGetWorkArea(int uiAction, int uiParam, out NativeRect pvParam, int fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfoSetWorkArea(int uiAction, int uiParam, ref NativeRect pvParam, int fWinIni);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
