using System.Globalization;
using System.Windows.Data;
using MarketPos.Services;

namespace MarketPos.Converters;

/// <summary>
/// A product's category as the shop reads it.
///
/// A product whose category was deleted, or that arrived on a delivery without one, sits on
/// the category row with no name. Shown as it is, that is an empty space where the category
/// should be; this says "No category" instead, in the shop's language.
///
/// Only for showing. The product itself keeps the blank name, because the forms that edit it
/// read the same field, and a translated label saved back would become a real category.
/// </summary>
public sealed class CategoryNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string name && name.Trim().Length > 0 ? name : Loc.T("No category");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
