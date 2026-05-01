using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CoreDesk_App.Converters;

public sealed class IconPathToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasIcon = value is string path && !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        var invert = parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true;
        return hasIcon ^ invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
