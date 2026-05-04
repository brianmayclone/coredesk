using CoreDesk.LiquidGlass;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace CoreDesk.LiquidGlass.TestHost;

public static class Program
{
    private static TestApplication? _application;

    [STAThread]
    public static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _application = new TestApplication();
        });
    }
}

public sealed class TestApplication : Application
{
    private Window? _window;

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        var windowWidth = Math.Max(960, (int)Math.Round(screenWidth * 0.8));
        var windowHeight = Math.Max(540, (int)Math.Round(screenHeight * 0.8));
        var surfaceWidth = Math.Round(windowWidth * 0.86);
        var surfaceHeight = Math.Round(windowHeight * 0.58);
        var options = LiquidGlassOptions.Default with
        {
            SurfaceWidth = surfaceWidth,
            SurfaceHeight = surfaceHeight,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            CornerRadius = Math.Max(58, (float)Math.Round(surfaceHeight * 0.13)),
            BlurAmount = 42,
            Saturation = 1.72f,
            Contrast = 1.22f,
            TintOpacity = 0.16f,
            EdgeLightOpacity = 0.55f,
            RefractionStrength = 0.026f,
            InnerShadowOpacity = 0.42f
        };

        _window = new LiquidGlassWindow(options);
        _window.Activate();
        NativeWindowChrome.Move(_window, (screenWidth - options.WindowWidth) / 2, (screenHeight - options.WindowHeight) / 2, options.WindowWidth, options.WindowHeight);
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
