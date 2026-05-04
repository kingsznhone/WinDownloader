using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace WindowsImageDownloader.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is bool boolValue && boolValue;
        if (parameter is string text &&
            string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is Visibility visibility && visibility == Visibility.Visible;
        if (parameter is string text &&
            string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible;
    }
}
