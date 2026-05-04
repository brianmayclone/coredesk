using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System.Collections;

namespace CoreDesk_App.Converters;

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var count = value switch
        {
            int number => number,
            ICollection collection => collection.Count,
            _ => 0
        };

        var visible = count > 0;
        if (string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
