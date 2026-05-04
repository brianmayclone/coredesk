using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CoreDesk.LiquidGlass;

public sealed class LiquidGlassWindow : Window
{
    private readonly LiquidGlassOptions _options;

    public LiquidGlassWindow(LiquidGlassOptions? options = null)
    {
        _options = options ?? LiquidGlassOptions.Default;
        Title = "CoreDesk Liquid Glass";
        var surfaceHost = new LiquidGlassPanel
        {
            Width = _options.SurfaceWidth,
            Height = _options.SurfaceHeight,
            Options = _options,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        surfaceHost.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(_options.CornerRadius),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(122, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "Liquid Glass",
                FontSize = 38,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        Content = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            Children = { surfaceHost }
        };

        Activated += (_, _) =>
        {
            NativeWindowChrome.ConfigureTransparentBorderless(this, _options.WindowWidth, _options.WindowHeight);
        };
    }
}
