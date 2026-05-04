using CoreDesk.LiquidGlass;
using Microsoft.UI.Xaml;

namespace CoreDesk.LiquidGlass.TestHost;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var app = new TestApplication();
        });
    }
}

public sealed class TestApplication : Application
{
    private Window? _window;

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var options = LiquidGlassOptions.Default with
        {
            SurfaceWidth = 860,
            SurfaceHeight = 190,
            WindowWidth = 1060,
            WindowHeight = 380,
            CornerRadius = 58,
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
        NativeWindowChrome.Move(_window, 420, 420, options.WindowWidth, options.WindowHeight);
    }
}
