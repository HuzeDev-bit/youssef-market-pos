using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// Enter a remise as either a percentage or a flat amount. The running calculation is shown
/// live, because the cashier is usually agreeing the figure with the customer standing there.
/// </summary>
public partial class DiscountWindow : Window
{
    private static readonly Regex Numeric = new(@"^[0-9]*[.,]?[0-9]{0,2}$", RegexOptions.Compiled);

    private readonly decimal _gross;

    public DiscountKind Kind { get; private set; } = DiscountKind.Percent;
    public decimal Value { get; private set; }

    /// <summary>True when the cashier chose to take the remise off entirely.</summary>
    public bool Removed { get; private set; }

    public DiscountWindow(decimal gross, DiscountKind currentKind, decimal currentValue)
    {
        InitializeComponent();
        _gross = gross;

        if (currentKind == DiscountKind.Fixed)
        {
            FixedButton.IsChecked = true;
            PercentButton.IsChecked = false;
            Kind = DiscountKind.Fixed;
        }

        RemoveButton.Visibility = currentKind == DiscountKind.None ? Visibility.Collapsed : Visibility.Visible;
        ValueBox.Text = currentValue > 0 ? currentValue.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

        Recalculate();
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    /// <summary>Shows the dialog; returns false when the cashier cancels.</summary>
    public static bool Ask(Window owner, decimal gross, DiscountKind kind, decimal value,
                           out DiscountKind newKind, out decimal newValue)
    {
        var dialog = new DiscountWindow(gross, kind, value) { Owner = owner };
        var confirmed = dialog.ShowDialog() == true;
        newKind = dialog.Removed ? DiscountKind.None : dialog.Kind;
        newValue = dialog.Removed ? 0m : dialog.Value;
        return confirmed;
    }

    private void Percent_Checked(object sender, RoutedEventArgs e)
    {
        if (FixedButton is null) return;
        FixedButton.IsChecked = false;
        Kind = DiscountKind.Percent;
        FieldLabel.Text = "Discount percentage";
        Recalculate();
    }

    private void Fixed_Checked(object sender, RoutedEventArgs e)
    {
        PercentButton.IsChecked = false;
        Kind = DiscountKind.Fixed;
        FieldLabel.Text = "Discount amount in DH";
        Recalculate();
    }

    private void ValueBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var proposed = ValueBox.Text.Remove(ValueBox.SelectionStart, ValueBox.SelectionLength)
                                    .Insert(ValueBox.SelectionStart, e.Text);
        e.Handled = !Numeric.IsMatch(proposed);
    }

    private void ValueBox_TextChanged(object sender, TextChangedEventArgs e) => Recalculate();

    private void Recalculate()
    {
        decimal.TryParse((ValueBox.Text ?? string.Empty).Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out var value);
        Value = value;

        var raw = Kind == DiscountKind.Percent ? _gross * value / 100m : value;
        var amount = Math.Round(Math.Clamp(raw, 0m, _gross), 2);   // never exceeds the basket

        GrossText.Text = Money(_gross);
        DiscountLabelText.Text = Kind == DiscountKind.Percent
            ? Loc.T("Remise ({0}%)", Loc.Ltr($"{value:0.##}"))
            : Loc.T("Remise ({0} DH)", Loc.Ltr($"{value:0.00}"));
        DiscountText.Text = "-" + Money(amount);
        TotalText.Text = Money(_gross - amount);

        ApplyButton.IsEnabled = value > 0;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        Removed = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
        else if (e.Key == Key.Enter && ApplyButton.IsEnabled) Apply_Click(sender, e);
    }

    private static string Money(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture) + " DH";
}
