using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System.Drawing;
using Windows.UI;

namespace CoreDesk_App.Converters;

public sealed class IconPathToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(92, 255, 255, 255));
        }

        if (Cache.TryGetValue(path, out var brush))
        {
            return brush;
        }

        try
        {
            using var bitmap = new Bitmap(path);
            var red = 0L;
            var green = 0L;
            var blue = 0L;
            var samples = 0;
            var stepX = Math.Max(1, bitmap.Width / 12);
            var stepY = Math.Max(1, bitmap.Height / 12);

            for (var y = 0; y < bitmap.Height; y += stepY)
            {
                for (var x = 0; x < bitmap.Width; x += stepX)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.A < 64)
                    {
                        continue;
                    }

                    red += pixel.R;
                    green += pixel.G;
                    blue += pixel.B;
                    samples++;
                }
            }

            if (samples == 0)
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(92, 255, 255, 255));
            }

            var color = Windows.UI.Color.FromArgb(
                128,
                Soften((int)(red / samples)),
                Soften((int)(green / samples)),
                Soften((int)(blue / samples)));
            brush = new SolidColorBrush(color);
            Cache[path] = brush;
            return brush;
        }
        catch
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(92, 255, 255, 255));
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static byte Soften(int value)
    {
        return (byte)Math.Clamp((value * 0.72) + 52, 44, 230);
    }
}
