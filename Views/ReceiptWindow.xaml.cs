using System.Windows;
using System.Windows.Input;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// Shows one receipt.
///
/// Straight after payment it shows the ORIGINAL, unstamped. Opened from the Tickets page it
/// is a past sale, so printing from there stamps DUPLICATA — a copy must never be mistakable
/// for a second sale at cash-up. Either way this window only ever reads: it cannot create a
/// sale, charge again, move stock or change revenue.
/// </summary>
public partial class ReceiptWindow : Window
{
    private readonly Receipt _receipt;
    private readonly bool _asDuplicate;

    public ReceiptWindow(Receipt receipt, bool allowReprint = false)
    {
        InitializeComponent();
        _receipt = receipt;
        _asDuplicate = allowReprint;

        HeadingText.Text = $"Receipt #{receipt.InvoiceNumber}";
        SubText.Text = allowReprint
            ? $"{receipt.SoldAt:dd/MM/yyyy HH:mm}  ·  {receipt.Total:N2} DH  ·  reprints as a duplicate"
            : $"{receipt.SoldAt:dd/MM/yyyy HH:mm}  ·  {receipt.Total:N2} DH";

        PrintButton.Content = allowReprint ? "Reprint" : "Print";
        Preview.Document = ReceiptPrinter.Build(receipt, _asDuplicate);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var error = ReceiptPrinter.PrintSilent(_receipt, _asDuplicate);
        if (error is not null) SubText.Text = error;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter) Close();
    }
}
