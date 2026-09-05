using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// Find a past sale by ticket number and print another copy of it.
///
/// Strictly read-only: it calls SaleRepository.FindByInvoiceNumber and nothing else, so a
/// reprint cannot create a sale, charge again, move stock or change revenue. The copy is
/// stamped DUPLICATA so it cannot be mistaken for a new transaction at cash-up.
/// </summary>
public partial class ReprintWindow : Window
{
    private static readonly Regex Digits = new(@"^[0-9]*$", RegexOptions.Compiled);

    private Receipt? _receipt;

    public ReprintWindow()
    {
        InitializeComponent();
        LoadRecent();
        Loaded += (_, _) => NumberBox.Focus();
    }

    private void LoadRecent()
    {
        var recent = SaleRepository.RecentInvoiceNumbers();
        RecentList.Items.Clear();

        foreach (var number in recent)
        {
            var captured = number;
            var button = new Button
            {
                Style = (Style)FindResource("Button.PillMuted"),
                Content = "#" + number,
                FontSize = 13,
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 8, 8),
            };
            button.Click += (_, _) => Load(captured);
            RecentList.Items.Add(button);
        }

        if (recent.Count == 0)
            RecentList.Items.Add(new TextBlock
            {
                Text = "No completed sales yet. Finish a sale with Pay and its receipt "
                     + "number will appear here.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
                Style = (Style)FindResource("Text.Muted"),
            });
    }

    private void Load(int invoiceNumber)
    {
        NumberBox.Text = invoiceNumber.ToString();
        _receipt = SaleRepository.FindByInvoiceNumber(invoiceNumber);

        if (_receipt is null)
        {
            PreviewTray.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            EmptyText.Text = $"No receipt #{invoiceNumber}. Receipt numbers are printed on completed sales — they are not the same as a held ticket.";
            PrintButton.IsEnabled = false;
            return;
        }

        // Preview shows the duplicate stamp too, so the cashier sees exactly what prints.
        Preview.Document = ReceiptPrinter.Build(_receipt, isDuplicate: true);
        PreviewTray.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;
        PrintButton.IsEnabled = true;
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(NumberBox.Text, out var number)) Load(number);
    }

    private void NumberBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Find_Click(sender, e);
    }

    private void NumberBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !Digits.IsMatch(e.Text);

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_receipt is null) return;
        var error = ReceiptPrinter.PrintSilent(_receipt, isDuplicate: true);
        EmptyText.Text = error ?? string.Empty;
        if (error is not null)
        {
            EmptyText.Visibility = Visibility.Visible;
            PreviewTray.Visibility = Visibility.Collapsed;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
