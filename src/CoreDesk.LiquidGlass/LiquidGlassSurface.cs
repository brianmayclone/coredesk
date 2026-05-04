using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Numerics;

namespace CoreDesk.LiquidGlass;

public sealed class LiquidGlassSurface : IDisposable
{
    private readonly FrameworkElement _host;
    private readonly Compositor _compositor;
    private readonly LiquidGlassOptions _options;
    private readonly SpriteVisual _backdropVisual;
    private readonly SpriteVisual _tintVisual;
    private readonly SpriteVisual _refractionVisual;
    private readonly SpriteVisual _edgeVisual;
    private readonly SpriteVisual _innerShadowVisual;
    private bool _disposed;

    public LiquidGlassSurface(FrameworkElement host, LiquidGlassOptions? options = null)
    {
        _host = host;
        _options = options ?? LiquidGlassOptions.Default;
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

        _backdropVisual = _compositor.CreateSpriteVisual();
        _backdropVisual.Brush = CreateLiquidGlassBrush();

        _tintVisual = _compositor.CreateSpriteVisual();
        _tintVisual.Brush = _compositor.CreateColorBrush(ToColor(_options.TintColor, _options.TintOpacity));

        _refractionVisual = _compositor.CreateSpriteVisual();
        _refractionVisual.Brush = CreateRefractionBrush();

        _edgeVisual = _compositor.CreateSpriteVisual();
        _edgeVisual.Brush = CreateEdgeLightBrush();
        _edgeVisual.Opacity = _options.EdgeLightOpacity;

        _innerShadowVisual = _compositor.CreateSpriteVisual();
        _innerShadowVisual.Brush = CreateInnerShadowBrush();
        _innerShadowVisual.Opacity = _options.InnerShadowOpacity;

        var root = _compositor.CreateContainerVisual();
        root.Children.InsertAtTop(_innerShadowVisual);
        root.Children.InsertAtTop(_edgeVisual);
        root.Children.InsertAtTop(_refractionVisual);
        root.Children.InsertAtBottom(_tintVisual);
        root.Children.InsertAtBottom(_backdropVisual);
        ElementCompositionPreview.SetElementChildVisual(host, root);

        host.SizeChanged += OnSizeChanged;
        Resize((float)host.ActualWidth, (float)host.ActualHeight);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _host.SizeChanged -= OnSizeChanged;
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _disposed = true;
    }

    private CompositionEffectBrush CreateLiquidGlassBrush()
    {
        var backdrop = _compositor.CreateBackdropBrush();
        var effect = new ColorMatrixEffect
        {
            Name = "liquidBrightness",
            ColorMatrix = new Matrix5x4
            {
                M11 = 1f,
                M22 = 1f,
                M33 = 1f,
                M44 = 1f,
                M51 = _options.Brightness,
                M52 = _options.Brightness,
                M53 = _options.Brightness
            },
            Source = new ContrastEffect
            {
                Name = "liquidContrast",
                Contrast = _options.Contrast,
                Source = new SaturationEffect
                {
                    Name = "liquidSaturation",
                    Saturation = _options.Saturation,
                    Source = new GaussianBlurEffect
                    {
                        Name = "liquidBlur",
                        BlurAmount = _options.BlurAmount,
                        BorderMode = EffectBorderMode.Hard,
                        Optimization = EffectOptimization.Balanced,
                        Source = new CompositionEffectSourceParameter("backdrop")
                    }
                }
            }
        };

        var factory = _compositor.CreateEffectFactory(
            effect,
            [
                "liquidBlur.BlurAmount",
                "liquidSaturation.Saturation",
                "liquidContrast.Contrast",
                "liquidBrightness.ColorMatrix"
            ]);
        var brush = factory.CreateBrush();
        brush.SetSourceParameter("backdrop", backdrop);
        return brush;
    }

    private CompositionBrush CreateEdgeLightBrush()
    {
        var brush = _compositor.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0.08f, 0f);
        brush.EndPoint = new Vector2(0.92f, 1f);
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(0f, Windows.UI.Color.FromArgb(190, 255, 255, 255)));
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(0.48f, Windows.UI.Color.FromArgb(26, 255, 255, 255)));
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(1f, Windows.UI.Color.FromArgb(110, 255, 255, 255)));
        return brush;
    }

    private CompositionEffectBrush CreateRefractionBrush()
    {
        var backdrop = _compositor.CreateBackdropBrush();
        var effect = new ColorMatrixEffect
        {
            Name = "liquidRefraction",
            ColorMatrix = new Matrix5x4
            {
                M11 = 1f + _options.RefractionStrength,
                M22 = 1f + _options.RefractionStrength,
                M33 = 1f + _options.RefractionStrength,
                M44 = 0.18f,
                M51 = _options.RefractionStrength,
                M52 = _options.RefractionStrength,
                M53 = _options.RefractionStrength
            },
            Source = new GaussianBlurEffect
            {
                Name = "liquidRefractionBlur",
                BlurAmount = Math.Max(1f, _options.BlurAmount * 0.18f),
                BorderMode = EffectBorderMode.Hard,
                Source = new CompositionEffectSourceParameter("backdrop")
            }
        };

        var factory = _compositor.CreateEffectFactory(effect, ["liquidRefraction.ColorMatrix"]);
        var brush = factory.CreateBrush();
        brush.SetSourceParameter("backdrop", backdrop);
        return brush;
    }

    private CompositionBrush CreateInnerShadowBrush()
    {
        var brush = _compositor.CreateRadialGradientBrush();
        brush.EllipseCenter = new Vector2(0.5f, 0.5f);
        brush.EllipseRadius = new Vector2(0.72f, 0.92f);
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(0f, Windows.UI.Color.FromArgb(0, 0, 0, 0)));
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(0.72f, Windows.UI.Color.FromArgb(0, 0, 0, 0)));
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(1f, Windows.UI.Color.FromArgb(120, 255, 255, 255)));
        return brush;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        Resize((float)args.NewSize.Width, (float)args.NewSize.Height);
    }

    private void Resize(float width, float height)
    {
        var size = new Vector2(Math.Max(1, width), Math.Max(1, height));
        _backdropVisual.Size = size;
        _tintVisual.Size = size;
        _refractionVisual.Size = size;
        _edgeVisual.Size = size;
        _innerShadowVisual.Size = size;
        TryApplyRoundedClip(_backdropVisual, size);
        TryApplyRoundedClip(_tintVisual, size);
        TryApplyRoundedClip(_refractionVisual, size);
        TryApplyRoundedClip(_edgeVisual, size);
        TryApplyRoundedClip(_innerShadowVisual, size);
    }

    private void TryApplyRoundedClip(Visual visual, Vector2 size)
    {
        var geometry = _compositor.CreateRoundedRectangleGeometry();
        geometry.Size = size;
        geometry.CornerRadius = new Vector2(_options.CornerRadius);
        var clip = _compositor.CreateGeometricClip(geometry);
        visual.Clip = clip;
    }

    private static Windows.UI.Color ToColor(Vector4 color, float opacity)
    {
        static byte Channel(float value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
        return Windows.UI.Color.FromArgb(Channel(opacity), Channel(color.X), Channel(color.Y), Channel(color.Z));
    }
}
