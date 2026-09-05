using System.Globalization;
using System.Windows.Data;

namespace MarketPos.Converters;

/// <summary>
/// Formats a MAD amount with its sign kept — "+120.00 DH", "−45.00 DH".
///
/// Used wherever the direction is the point: a cash movement, a till difference, a change
/// against the previous period. A bare "45.00 DH" against an expected figure tells the owner
/// nothing about whether they are up or down.
/// </summary>
public sealed class SignedMoneyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal amount) return string.Empty;

        var sign = amount > 0 ? "+" : amount < 0 ? "−" : string.Empty;   // U+2212, not a hyphen
        return Services.Loc.Ltr(
            $"{sign}{Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture)} DH");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
