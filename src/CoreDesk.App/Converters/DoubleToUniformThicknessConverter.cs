using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CoreDesk_App.Converters;

public sealed class DoubleToUniformThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is double size ? new Thickness(size) : new Thickness(42);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
