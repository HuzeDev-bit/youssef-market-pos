using System.Windows;
using System.Windows.Input;

namespace MarketPos.Views;

/// <summary>
/// Waits for a scan and hands back the code.
///
/// A barcode scanner is a keyboard, so there is no device to talk to and nothing to watch —
/// which is exactly the problem. Without this, "click the field and scan" gives the cashier no
/// sign the till is listening. This window is the sign: it takes the keystrokes, closes itself
/// the moment a code arrives, and can be typed into when the barcode is scuffed.
/// </summary>
public partial class ScanWindow : Window
{
    /// <summary>The scanned code. Empty when the cashier said the product has no barcode.</summary>
    public string Code { get; private set; } = string.Empty;

    public ScanWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { CodeBox.Focus(); Keyboard.Focus(CodeBox); };
    }

    /// <summary>
    /// Shows the prompt. Returns the scanned code, an empty string when the product has no
    /// barcode, or null when the cashier backed out entirely — three different answers that
    /// the caller has to tell apart.
    /// </summary>
    public static string? Ask(Window owner)
    {
        var window = new ScanWindow { Owner = owner };
        return window.ShowDialog() == true ? window.Code : null;
    }

    /// <summary>A scanner finishes with Enter, so this is the normal way the window closes.</summary>
    private void Code_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Use_Click(sender, e);
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (code.Length == 0) return;   // nothing scanned yet; keep waiting

        Code = code;
        DialogResult = true;
        Close();
    }

    private void NoBarcode_Click(object sender, MouseButtonEventArgs e)
    {
        Code = string.Empty;
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
    }
}
