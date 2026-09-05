using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Views.Admin;

/// <summary>
/// Records one supplier invoice: several products, arriving together, on one piece of paper.
///
/// Saving does three things at once, in one transaction — the invoice, the stock it added,
/// and whatever was paid at the door. The unpaid remainder becomes supplier debt rather than
/// an expense, because the money has not left yet.
/// </summary>
public partial class PurchaseWindow : Window
{
    public PurchaseWindow(int? supplierId)
    {
        InitializeComponent();

        var suppliers = SupplierRepository.List();
        SupplierBox.ItemsSource = suppliers;
        SupplierBox.SelectedItem = suppliers.FirstOrDefault(s => s.Id == supplierId) ?? suppliers.FirstOrDefault();

        MethodBox.ItemsSource = new[] { "Cash", "Bank transfer", "Cheque", "Card", "Credit — pay later" };
        MethodBox.SelectedIndex = 0;

        DateBox.SelectedDate = DateTime.Today;
        PaidBox.Text = "0";

        UpdateTotals();
        Loaded += (_, _) => Editor.FocusProduct();
    }

    public static bool Show(Window owner, int? supplierId = null)
    {
        if (SupplierRepository.List().Count == 0)
        {
            ConfirmWindow.Ask(owner, "Add a supplier first",
                "A delivery has to belong to someone. Add the supplier, then record what they brought.");
            return false;
        }

        // A delivery is made of products. On a shop that has not entered any yet this dialog
        // would open with an empty list and no way to add a line — an unexplained dead end,
        // and exactly what a brand new shop meets first.
        if (StockRepository.List().Count == 0)
        {
            ConfirmWindow.Ask(owner, "Add some products first",
                "A delivery is a list of things the shop sells. Put them in under Add product, "
                + "then come back and record what arrived.");
            return false;
        }

        return new PurchaseWindow(supplierId) { Owner = owner }.ShowDialog() == true;
    }

    // ------------------------------- Lines -------------------------------

    private void Editor_Problem(object? sender, string problem) => ErrorText.Text = problem;

    private void Editor_Changed(object? sender, EventArgs e)
    {
        ErrorText.Text = string.Empty;
        UpdateTotals();
    }

    private void Paid_Changed(object sender, RoutedEventArgs e) => UpdateTotals();

    private void UpdateTotals()
    {
        if (TotalText is null || Editor is null) return;

        var total = Editor.Total;
        TotalText.Text = $"{total:N2} DH";

        DeliveryEditor.TryMoney(PaidBox.Text, out var paid);
        var remaining = total - paid;

        OwingText.Text = Editor.Lines.Count == 0
            ? "Add the products that arrived."
            : remaining <= 0m
                ? "Paid in full — nothing will be owed."
                : $"{remaining:N2} DH will be owed to this supplier.";

        OwingText.Foreground = (System.Windows.Media.Brush)FindResource(
            remaining > 0m && Editor.Lines.Count > 0 ? "Brush.Danger" : "Brush.Muted");
    }

    // ------------------------------- Saving -------------------------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (SupplierBox.SelectedItem is not Supplier supplier)
        {
            ErrorText.Text = "Choose the supplier this delivery came from.";
            return;
        }
        if (Editor.Lines.Count == 0)
        {
            ErrorText.Text = "Add at least one product line.";
            return;
        }

        var total = Editor.Total;
        DeliveryEditor.TryMoney(PaidBox.Text, out var paid);

        if (paid < 0m)
        {
            ErrorText.Text = "The amount paid cannot be negative.";
            return;
        }
        if (paid > total)
        {
            ErrorText.Text = $"You cannot pay more than the {total:N2} DH invoice.";
            return;
        }

        var purchase = new Purchase
        {
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            InvoiceNumber = InvoiceBox.Text.Trim(),
            PurchasedOn = DateBox.SelectedDate ?? DateTime.Today,
            DueOn = DueBox.SelectedDate,
            Method = MethodBox.SelectedItem as string ?? "Cash",
            Note = NoteBox.Text.Trim(),
            Lines = Editor.Lines.ToList(),
        };

        var atALoss = Editor.BelowCost;

        if (atALoss.Count > 0 &&
            !ConfirmWindow.Ask(this,
                atALoss.Count == 1
                    ? $"Sell {atALoss[0].Name} below what it cost?"
                    : $"Sell {atALoss.Count} of these below what they cost?",
                "Every one sold will lose money. Sometimes that is deliberate \u2014 confirm if it is."))
            return;

        try
        {
            SupplierRepository.RecordPurchase(purchase, paid);
            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Backing out of a half-entered invoice loses real typing, so it asks first.
        var count = Editor.Lines.Count;
        if (count > 0 &&
            !ConfirmWindow.Ask(this, "Discard this delivery?",
                $"{count} line{(count == 1 ? string.Empty : "s")} will be lost."))
            return;

        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
        else if (e.Key == Key.Enter && Editor.WantsEnter) Editor_Problem(this, Editor.AddLine() ?? string.Empty);
    }
}
