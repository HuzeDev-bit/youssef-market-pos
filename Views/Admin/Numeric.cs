using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MarketPos.Views.Admin;

/// <summary>
/// Makes a text box refuse anything that is not a number, keystroke by keystroke.
///
/// Prices and quantities were plain text boxes, so "bought for" would take a word and only
/// complain on Save — by which point the cashier has typed a whole product in and is being
/// sent back to a box they thought was fine. Refusing the keystroke says it at the moment it
/// happens, and there is nothing to explain.
///
/// Written as an attached property so any back-office form can ask for it in markup:
/// <c>local:Numeric.Only="True"</c>.
/// </summary>
public static class Numeric
{
    public static readonly DependencyProperty OnlyProperty = DependencyProperty.RegisterAttached(
        "Only", typeof(bool), typeof(Numeric), new PropertyMetadata(false, OnOnlyChanged));

    public static void SetOnly(DependencyObject element, bool value) =>
        element.SetValue(OnlyProperty, value);

    public static bool GetOnly(DependencyObject element) => (bool)element.GetValue(OnlyProperty);

    private static void OnOnlyChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box) return;

        if ((bool)e.NewValue)
        {
            box.PreviewTextInput += Typed;
            box.PreviewKeyDown += KeyPressed;
            DataObject.AddPastingHandler(box, Pasted);
        }
        else
        {
            box.PreviewTextInput -= Typed;
            box.PreviewKeyDown -= KeyPressed;
            DataObject.RemovePastingHandler(box, Pasted);
        }
    }

    private static void Typed(object sender, TextCompositionEventArgs e)
    {
        var box = (TextBox)sender;
        e.Handled = !IsANumber(Resulting(box, e.Text));
    }

    /// <summary>Space does not raise PreviewTextInput in a TextBox, so it is stopped here.</summary>
    private static void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) e.Handled = true;
    }

    private static void Pasted(object sender, DataObjectPastingEventArgs e)
    {
        var box = (TextBox)sender;
        var pasted = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
            ? (string)e.DataObject.GetData(DataFormats.UnicodeText)!
            : string.Empty;

        if (!IsANumber(Resulting(box, pasted))) e.CancelCommand();
    }

    /// <summary>What the box would read once this text replaced whatever is selected.</summary>
    private static string Resulting(TextBox box, string incoming) =>
        box.Text.Remove(box.SelectionStart, box.SelectionLength)
                .Insert(box.SelectionStart, incoming);

    /// <summary>
    /// True for anything that is on its way to being a number. A half-typed "8." has to pass
    /// or the decimal point could never be reached; the parse on Save is what decides whether
    /// it finished. Both separators are allowed — a Moroccan counter sees both keyboards.
    ///
    /// Only 0-9, deliberately: char.IsDigit is true for Arabic-Indic ٠١٢ as well, which the
    /// invariant-culture parse on Save then refuses. Letting them into the box would hand
    /// back the same late complaint this whole rule exists to remove.
    /// </summary>
    internal static bool IsANumber(string text)
    {
        var separators = 0;

        foreach (var character in text)
        {
            if (character is >= '0' and <= '9') continue;
            if (character is '.' or ',' && ++separators == 1) continue;
            return false;
        }

        return true;
    }
}
