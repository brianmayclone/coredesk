using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CoreDesk.UiTests;

public sealed class CoreDeskSmokeTests
{
    [Fact]
    public void DiagnosticsMode_CapturesCoreShellScreenshots()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));

        if (!ShouldRunUiTests())
        {
            return;
        }

        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var screenshotDirectory = Path.Combine(FindRepositoryRoot(), "artifacts", "screenshots", runId);
        Directory.CreateDirectory(screenshotDirectory);
        var launchArguments = Environment.GetEnvironmentVariable("COREDESK_APP_ARGS");
        if (string.IsNullOrWhiteSpace(launchArguments))
        {
            launchArguments = "--diagnostics --safe-mode --language en";
        }

        File.WriteAllText(Path.Combine(screenshotDirectory, "run-info.txt"), launchArguments);

        using var app = Application.Launch(new System.Diagnostics.ProcessStartInfo
        {
            FileName = GetAppExecutablePath(),
            Arguments = launchArguments,
            UseShellExecute = true
        });
        using var automation = new UIA3Automation();
        var screenshotFailures = new List<string>();

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(20));

            Assert.NotNull(window);
            WaitForElementByName(window, "Open App Drawer", TimeSpan.FromSeconds(10));
            BringCoreDeskToFront();
            Thread.Sleep(2000);
            CapturePhysicalScreen(screenshotDirectory, "01-homescreen.png", screenshotFailures);

            ClickByName(window, "Open App Drawer");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "02-appdrawer.png", screenshotFailures);

            ClickByName(window, "Open Settings");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "03-settings.png", screenshotFailures);

            ClickByName(window, "Open Control Center");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "04-control-center.png", screenshotFailures);

            ClickByName(window, "Close Control Center");
            ClickByName(window, "Toggle Desktop Mode");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "05-desktop-dock-overlay.png", screenshotFailures);
        }
        finally
        {
            CloseApp(app);
        }

        Assert.Empty(screenshotFailures);
    }

    private static bool ShouldRunUiTests()
    {
        return string.Equals(Environment.GetEnvironmentVariable("COREDESK_RUN_UI_TESTS"), "1", StringComparison.OrdinalIgnoreCase);
    }

    private static void ClickByName(Window window, string name)
    {
        var button = WaitForElementByName(window, name, TimeSpan.FromSeconds(5))?.AsButton();
        Assert.NotNull(button);
        button.Invoke();
        Thread.Sleep(500);
    }

    private static AutomationElement? WaitForElementByName(Window window, string name, TimeSpan timeout)
    {
        var end = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < end)
        {
            var element = window.FindFirstDescendant(cf => cf.ByName(name).And(cf.ByControlType(ControlType.Button)));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(250);
        }

        return null;
    }

    private static void CapturePhysicalScreen(string directory, string fileName, List<string> failures)
    {
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height));
        }

        Assert.True(bitmap.Width >= 3000, $"Physical screenshot width was only {bitmap.Width}px.");
        Assert.True(bitmap.Height >= 1600, $"Physical screenshot height was only {bitmap.Height}px.");

        var isDesktopOverlay = fileName.Contains("desktop-dock-overlay", StringComparison.OrdinalIgnoreCase);
        var readabilityFailure = isDesktopOverlay ? null : GetReadabilityFailure(bitmap);
        if (readabilityFailure is not null)
        {
            failures.Add($"{fileName}: {readabilityFailure}");
        }

        if (fileName.Contains("homescreen", StringComparison.OrdinalIgnoreCase))
        {
            var dockFailure = GetDockSurfaceFailure(bitmap);
            if (dockFailure is not null)
            {
                failures.Add($"{fileName}: {dockFailure}");
            }
        }

        bitmap.Save(Path.Combine(directory, fileName), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void BringCoreDeskToFront()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("CoreDesk.App"))
        {
            if (process.MainWindowHandle == IntPtr.Zero)
            {
                continue;
            }

            ShowWindow(process.MainWindowHandle, 9);
            SetForegroundWindow(process.MainWindowHandle);
            Thread.Sleep(600);
            return;
        }
    }

    private static void AssertCoreDeskIsForeground()
    {
        var foreground = GetForegroundWindow();
        _ = GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        var coreDeskProcessIds = System.Diagnostics.Process.GetProcessesByName("CoreDesk.App")
            .Select(process => (uint)process.Id)
            .ToHashSet();
        Assert.Contains(foregroundProcessId, coreDeskProcessIds);
    }

    private static string? GetReadabilityFailure(Bitmap bitmap)
    {
        var sampled = 0;
        var bright = 0;
        var colorful = 0;
        var stepX = Math.Max(1, bitmap.Width / 80);
        var stepY = Math.Max(1, bitmap.Height / 45);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var luminance = (color.R * 0.2126) + (color.G * 0.7152) + (color.B * 0.0722);
                var spread = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
                sampled++;
                if (luminance > 70)
                {
                    bright++;
                }

                if (spread > 18)
                {
                    colorful++;
                }
            }
        }

        if (bright <= sampled * 0.02)
        {
            return "Screenshot is too dark to be useful.";
        }

        if (colorful <= sampled * 0.05)
        {
            return "Screenshot has too little visual variation to be useful.";
        }

        return null;
    }

    private static string? GetDockSurfaceFailure(Bitmap bitmap)
    {
        var region = new Rectangle(
            (int)(bitmap.Width * 0.34),
            (int)(bitmap.Height * 0.88),
            (int)(bitmap.Width * 0.32),
            (int)(bitmap.Height * 0.1));
        var sampled = 0;
        var veryBright = 0;
        var totalLuminance = 0.0;
        var totalSpread = 0.0;
        var stepX = Math.Max(1, region.Width / 90);
        var stepY = Math.Max(1, region.Height / 24);

        for (var y = region.Top; y < region.Bottom; y += stepY)
        {
            for (var x = region.Left; x < region.Right; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var luminance = (color.R * 0.2126) + (color.G * 0.7152) + (color.B * 0.0722);
                var spread = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
                sampled++;
                totalLuminance += luminance;
                totalSpread += spread;
                if (luminance > 225)
                {
                    veryBright++;
                }
            }
        }

        var averageLuminance = totalLuminance / sampled;
        var averageSpread = totalSpread / sampled;
        if (averageLuminance > 205 && veryBright > sampled * 0.35 && averageSpread < 28)
        {
            return "Dock surface is still rendering as a bright white block instead of a translucent blurred surface.";
        }

        return null;
    }

    private static void CloseApp(Application app)
    {
        try
        {
            app.Close();
            Thread.Sleep(500);
        }
        catch
        {
            // The fallback below handles stubborn WinUI processes.
        }

        foreach (var process in System.Diagnostics.Process.GetProcessesByName("CoreDesk.App"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup for test isolation.
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    private static string GetAppExecutablePath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("COREDESK_APP_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        var root = FindRepositoryRoot();
        var candidates = Directory.EnumerateFiles(Path.Combine(root, "src", "CoreDesk.App", "bin"), "CoreDesk.App.exe", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new FileNotFoundException("CoreDesk.App.exe was not found. Build CoreDesk.App before running UI tests.");
        }

        return candidates[0];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CoreDesk.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CoreDesk.sln.");
    }
}
