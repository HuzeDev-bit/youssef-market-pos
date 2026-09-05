using System.Windows;
using System.Windows.Input;

namespace MarketPos.Views.Admin;

/// <summary>
/// Sets a cashier's till PIN.
///
/// Four digits is the practical floor for something typed at a counter with a queue behind
/// it. It is not a strong secret and is not pretending to be one — it identifies who is on
/// the till, so sales and drawer differences land against a name. The owner's password, which
/// protects the money screens, is a different thing entirely.
/// </summary>
public partial class PinWindow : Window
{
    private string? _pin;

    public PinWindow(string workerName, bool hasPin)
    {
        InitializeComponent();

        HeadingText.Text = hasPin ? "Change till PIN" : "Set a till PIN";
        SubText.Text = $"{workerName} types this to sign in at the till, so their sales and shifts "
                     + "are recorded against them."
                     + (hasPin ? " The old PIN stops working straight away." : string.Empty);
        Title = HeadingText.Text;

        Loaded += (_, _) => PinBox.Focus();
    }

    /// <summary>Returns the new PIN, or null if the owner backed out.</summary>
    public static string? Ask(Window owner, string workerName, bool hasPin)
    {
        var window = new PinWindow(workerName, hasPin) { Owner = owner };
        return window.ShowDialog() == true ? window._pin : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var pin = PinBox.Password;

        if (pin.Length < 4)
        {
            Fail("Use at least 4 characters.");
            return;
        }
        if (pin != ConfirmBox.Password)
        {
            Fail("The two PINs do not match.");
            return;
        }

        _pin = pin;
        DialogResult = true;
        Close();
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        PinBox.Clear();
        ConfirmBox.Clear();
        PinBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
        else if (e.Key == Key.Enter) Save_Click(sender, e);
    }
}
