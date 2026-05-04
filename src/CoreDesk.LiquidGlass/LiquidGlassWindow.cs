using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CoreDesk.LiquidGlass;

public sealed class LiquidGlassWindow : Window
{
    private readonly Border _surfaceHost;
    private readonly LiquidGlassOptions _options;
    private LiquidGlassSurface? _surface;

    public LiquidGlassWindow(LiquidGlassOptions? options = null)
    {
        _options = options ?? LiquidGlassOptions.Default;
        Title = "CoreDesk Liquid Glass";
        _surfaceHost = new Border
        {
            Width = _options.SurfaceWidth,
            Height = _options.SurfaceHeight,
            CornerRadius = new CornerRadius(_options.CornerRadius),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(122, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Liquid Glass",
                        FontSize = 38,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };

        Content = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            Children = { _surfaceHost }
        };

        Activated += (_, _) =>
        {
            NativeWindowChrome.ConfigureTransparentBorderless(this, _options.WindowWidth, _options.WindowHeight);
            _surface ??= new LiquidGlassSurface(_surfaceHost, _options);
        };
        Closed += (_, _) => _surface?.Dispose();
    }
}
