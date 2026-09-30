// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace WinDownloader.Converters;

public partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isVisible = value is bool boolValue && boolValue;
        if (parameter is string text &&
            string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        bool isVisible = value is Visibility visibility && visibility == Visibility.Visible;
        if (parameter is string text &&
            string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible;
    }
}
