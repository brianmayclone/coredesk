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

    private NotifyIcon? _trayIcon;
    private bool _disposed;

    public event EventHandler<SystemIntegrationCommand>? CommandRequested;

    public void Initialize()
    {
        ShowTrayIcon();
    }

    public void SetTaskbarVisible(bool visible)
    {
        foreach (var taskbarHandle in EnumerateTaskbarWindows())
        {
            ShowWindow(taskbarHandle, visible ? SW_SHOW : SW_HIDE);
            EnableWindow(taskbarHandle, visible);
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
        SetTaskbarVisible(true);
        _trayIcon?.Dispose();
    }

    private void Request(SystemIntegrationCommand command)
    {
        CommandRequested?.Invoke(this, command);
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
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}
