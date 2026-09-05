using System.Globalization;
using System.Windows.Data;

namespace MarketPos.Converters;

/// <summary>Formats a decimal MAD amount as "12.50 DH".</summary>
public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d
            ? Services.Loc.Ltr($"{d.ToString("N2", CultureInfo.InvariantCulture)} DH")
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
