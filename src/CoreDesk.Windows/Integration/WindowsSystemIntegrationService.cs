using System.Runtime.InteropServices;
using System.Windows.Forms;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Windows.Integration;

public sealed class WindowsSystemIntegrationService : ISystemIntegrationService
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private NotifyIcon? _trayIcon;

    public event EventHandler<SystemIntegrationCommand>? CommandRequested;

    public void Initialize()
    {
        ShowTrayIcon();
    }

    public void SetTaskbarVisible(bool visible)
    {
        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle != IntPtr.Zero)
        {
            ShowWindow(taskbarHandle, visible ? SW_SHOW : SW_HIDE);
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
        SetTaskbarVisible(true);
        _trayIcon?.Dispose();
    }

    private void Request(SystemIntegrationCommand command)
    {
        CommandRequested?.Invoke(this, command);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
