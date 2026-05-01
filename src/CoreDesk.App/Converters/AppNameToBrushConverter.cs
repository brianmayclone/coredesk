using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CoreDesk_App.Converters;

public sealed class AppNameToBrushConverter : IValueConverter
{
    private static readonly Color[] Colors =
    [
        Color.FromArgb(218, 86, 132, 211),
        Color.FromArgb(218, 68, 158, 132),
        Color.FromArgb(218, 215, 137, 56),
        Color.FromArgb(218, 146, 91, 176),
        Color.FromArgb(218, 199, 86, 118),
        Color.FromArgb(218, 69, 142, 173),
        Color.FromArgb(218, 113, 126, 146),
        Color.FromArgb(218, 99, 166, 124)
    ];

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value?.ToString() ?? string.Empty;
        var hash = text.Aggregate(17, (current, character) => (current * 31) + character);
        var color = Colors[Math.Abs(hash) % Colors.Length];
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
