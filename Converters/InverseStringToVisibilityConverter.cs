using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MarketPos.Converters;

/// <summary>Visible when the bound string is empty — used to show the fallback glyph only while a product has no photo.</summary>
public sealed class InverseStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
