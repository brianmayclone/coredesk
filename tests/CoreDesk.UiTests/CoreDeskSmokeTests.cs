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

            ClickAnyByName(window, "Utilities");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "02-folder-overlay.png", screenshotFailures);

            ClickByName(window, "Close Folder");
            ClickByName(window, "Open App Drawer");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "03-appdrawer.png", screenshotFailures);

            EnterTextByName(window, "App Drawer Search", "store");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "04-appdrawer-search.png", screenshotFailures);

            ClickByName(window, "Open Settings");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "05-settings.png", screenshotFailures);

            ClickByName(window, "Close Settings");
            ClickByName(window, "Open Control Center");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "06-control-center.png", screenshotFailures);

            ClickByName(window, "Close Control Center");
            ClickByName(window, "Toggle Desktop Mode");
            BringCoreDeskToFront();
            CapturePhysicalScreen(screenshotDirectory, "07-desktop-dock-overlay.png", screenshotFailures);

            ClickDockByName(automation, "Open Task Switcher");
            CapturePhysicalScreen(screenshotDirectory, "08-task-switcher.png", screenshotFailures);
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

    private static void ClickAnyByName(Window window, string name)
    {
        var element = WaitForAnyElementByName(window, name, TimeSpan.FromSeconds(5));
        Assert.NotNull(element);
        element.Click();
        Thread.Sleep(500);
    }

    private static void EnterTextByName(Window window, string name, string value)
    {
        var element = WaitForElementByName(window, name, ControlType.Edit, TimeSpan.FromSeconds(5));
        Assert.NotNull(element);
        var textBox = element.AsTextBox();
        textBox.Focus();
        textBox.Text = value;
        Thread.Sleep(700);
    }

    private static AutomationElement? WaitForElementByName(Window window, string name, TimeSpan timeout)
    {
        return WaitForElementByName(window, name, ControlType.Button, timeout);
    }

    private static AutomationElement? WaitForElementByName(Window window, string name, ControlType controlType, TimeSpan timeout)
    {
        var end = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < end)
        {
            var element = window.FindFirstDescendant(cf => cf.ByName(name).And(cf.ByControlType(controlType)));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(250);
        }

        return null;
    }

    private static AutomationElement? WaitForAnyElementByName(Window window, string name, TimeSpan timeout)
    {
        var end = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < end)
        {
            var element = window.FindFirstDescendant(cf => cf.ByName(name));
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
        var isTaskSwitcher = fileName.Contains("task-switcher", StringComparison.OrdinalIgnoreCase);
        var readabilityFailure = isDesktopOverlay || isTaskSwitcher ? null : GetReadabilityFailure(bitmap);
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

        if (isDesktopOverlay)
        {
            var overlayDockFailure = GetDesktopOverlayDockFailure(bitmap);
            if (overlayDockFailure is not null)
            {
                failures.Add($"{fileName}: {overlayDockFailure}");
            }
        }

        if (isTaskSwitcher)
        {
            var taskSwitcherFailure = GetTaskSwitcherFailure(bitmap);
            if (taskSwitcherFailure is not null)
            {
                failures.Add($"{fileName}: {taskSwitcherFailure}");
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

    private static void ClickDockByName(UIA3Automation automation, string name)
    {
        var end = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < end)
        {
            var desktop = automation.GetDesktop();
            var button = desktop.FindFirstDescendant(cf => cf.ByName(name).And(cf.ByControlType(ControlType.Button)))?.AsButton();
            if (button is not null)
            {
                button.Invoke();
                Thread.Sleep(1400);
                return;
            }

            Thread.Sleep(250);
        }

        Assert.Fail($"Dock button '{name}' was not found.");
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

    private static string? GetDesktopOverlayDockFailure(Bitmap bitmap)
    {
        var region = new Rectangle(
            (int)(bitmap.Width * 0.4),
            (int)(bitmap.Height * 0.89),
            (int)(bitmap.Width * 0.24),
            (int)(bitmap.Height * 0.08));
        var sampled = 0;
        var bright = 0;
        var veryBright = 0;
        var totalLuminance = 0.0;
        var totalSpread = 0.0;
        var stepX = Math.Max(1, region.Width / 80);
        var stepY = Math.Max(1, region.Height / 22);

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
                if (luminance > 75)
                {
                    bright++;
                }

                if (luminance > 220)
                {
                    veryBright++;
                }
            }
        }

        var averageLuminance = totalLuminance / sampled;
        var averageSpread = totalSpread / sampled;
        if (averageLuminance < 48 || bright < sampled * 0.14)
        {
            return "Desktop overlay dock is not visibly rendered at the bottom of the physical screenshot.";
        }

        if (averageLuminance > 205 && veryBright > sampled * 0.35 && averageSpread < 30)
        {
            return "Desktop overlay dock is still rendering as a flat white block instead of liquid glass.";
        }

        return null;
    }

    private static string? GetTaskSwitcherFailure(Bitmap bitmap)
    {
        var region = new Rectangle(
            (int)(bitmap.Width * 0.03),
            (int)(bitmap.Height * 0.12),
            (int)(bitmap.Width * 0.48),
            (int)(bitmap.Height * 0.52));
        var sampled = 0;
        var bright = 0;
        var blueIndicators = 0;
        var stepX = Math.Max(1, region.Width / 120);
        var stepY = Math.Max(1, region.Height / 80);

        for (var y = region.Top; y < region.Bottom; y += stepY)
        {
            for (var x = region.Left; x < region.Right; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var luminance = (color.R * 0.2126) + (color.G * 0.7152) + (color.B * 0.0722);
                sampled++;
                if (luminance > 190)
                {
                    bright++;
                }

                if (color.B > 180 && color.G > 95 && color.R < 55)
                {
                    blueIndicators++;
                }
            }
        }

        if (bright < sampled * 0.03)
        {
            return "Task switcher did not render visible app cards.";
        }

        if (blueIndicators < 2)
        {
            return "Task switcher did not render running-app indicators.";
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
