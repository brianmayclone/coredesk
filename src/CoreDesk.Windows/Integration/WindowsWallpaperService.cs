using System.Runtime.InteropServices;
using System.Text;
using CoreDesk.Abstractions.Services;
using Microsoft.Win32;

namespace CoreDesk.Windows.Integration;

public sealed class WindowsWallpaperService : IWallpaperService
{
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int MaxPath = 260;

    public string? GetCurrentWallpaperPath()
    {
        var buffer = new StringBuilder(MaxPath);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, MaxPath, buffer, 0))
        {
            var path = buffer.ToString();
            if (File.Exists(path))
            {
                return path;
            }
        }

        var registryValue = Registry.CurrentUser
            .OpenSubKey(@"Control Panel\Desktop")
            ?.GetValue("WallPaper") as string;

        if (!string.IsNullOrWhiteSpace(registryValue) && File.Exists(registryValue))
        {
            return registryValue;
        }

        var transcoded = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Themes",
            "TranscodedWallpaper");

        return File.Exists(transcoded) ? transcoded : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(int action, int param, StringBuilder value, int winIni);
}
