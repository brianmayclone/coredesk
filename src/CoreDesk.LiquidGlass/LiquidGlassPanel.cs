using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CoreDesk.LiquidGlass;

public sealed class LiquidGlassPanel : Grid
{
    public static readonly DependencyProperty OptionsProperty =
        DependencyProperty.Register(
            nameof(Options),
            typeof(LiquidGlassOptions),
            typeof(LiquidGlassPanel),
            new PropertyMetadata(LiquidGlassOptions.Default, OnOptionsChanged));

    private LiquidGlassSurface? _surface;

    public LiquidGlassPanel()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public LiquidGlassOptions Options
    {
        get => (LiquidGlassOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    private static void OnOptionsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is LiquidGlassPanel panel && panel.IsLoaded)
        {
            panel.RecreateSurface();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RecreateSurface();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _surface?.Dispose();
        _surface = null;
    }

    private void RecreateSurface()
    {
        _surface?.Dispose();
        _surface = new LiquidGlassSurface(this, Options);
    }
}
