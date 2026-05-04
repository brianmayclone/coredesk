using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CoreDesk_App;

public sealed partial class AppIconContextMenuRow : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(AppIconContextMenuRow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(AppIconContextMenuRow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsDestructiveProperty = DependencyProperty.Register(
        nameof(IsDestructive),
        typeof(bool),
        typeof(AppIconContextMenuRow),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public AppIconContextMenuRow()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public bool IsDestructive
    {
        get => (bool)GetValue(IsDestructiveProperty);
        set => SetValue(IsDestructiveProperty, value);
    }

    public Brush ForegroundBrush => IsDestructive
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 59, 48))
        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 24, 32));

    private static void OnVisualPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is AppIconContextMenuRow row)
        {
            row.Bindings.Update();
        }
    }
}
