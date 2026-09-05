using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MarketPos.Converters;

/// <summary>True -> Collapsed, False -> Visible. Used to show the "cart is empty" placeholder.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
