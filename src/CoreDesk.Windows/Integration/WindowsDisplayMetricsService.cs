using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CoreDesk.Abstractions.Models;
using CoreDesk.Abstractions.Services;

namespace CoreDesk.Windows.Integration;

public sealed class WindowsDisplayMetricsService : IDisplayMetricsService
{
    public DisplayMetrics GetPrimaryDisplayMetrics()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        var dpiX = graphics.DpiX;
        var dpiY = graphics.DpiY;

        var hdc = graphics.GetHdc();
        try
        {
            var widthMm = GetDeviceCaps(hdc, 4);
            var heightMm = GetDeviceCaps(hdc, 6);
            return new DisplayMetrics(
                bounds.Width,
                bounds.Height,
                dpiX,
                dpiY,
                widthMm > 0 ? widthMm / 10.0 : null,
                heightMm > 0 ? heightMm / 10.0 : null);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);
}
