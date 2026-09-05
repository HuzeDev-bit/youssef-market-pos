using System.Windows;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Who the shop buys from, what was bought, and what is still owed.
///
/// A corner shop runs on credit with its wholesalers, so the number that matters is not what
/// was spent — it is what is still outstanding, per supplier, with the name and phone number
/// beside it. That is the list the owner reads before deciding who to pay this week.
///
/// Buying stock is not an expense: money leaves the till and goods arrive, so the shop is no
/// poorer for it. The cost lands in profit later, as cost of goods, when the item sells. That
/// is why nothing on this page appears in the expense figures, and why "bought" and "paid"
/// are shown apart — they answer different questions and only one of them is about cash.
/// </summary>
public partial class SuppliersPage : AdminPageBase
{
    /// <summary>One delivery, with what was actually in it.</summary>
    private sealed class Delivery
    {
        public required string When { get; init; }
        public required string Cost { get; init; }
        public required string Owing { get; init; }
        public bool StillOwing { get; init; }
        public required List<DeliveryLine> Lines { get; init; }
    }

    /// <summary>One product on a delivery: how many, at what each, for how much.</summary>
    private sealed class DeliveryLine
    {
        public required string What { get; init; }
        public required string Cost { get; init; }
    }

    private List<Supplier> _rows = new();
    private int _picked;

    public SuppliersPage() => InitializeComponent();

    public override string Title => "Suppliers";
    public override string Subtitle => "Who the shop buys from, and what it still owes them";

    protected override void Load()
    {
        Session.Require(Permission.ManageSuppliers);

        _rows = SupplierRepository.List(includeInactive: true, search: SearchBox.Text);

        if (OwedOnly.IsChecked == true)
            _rows = _rows.Where(s => s.Owed > 0m).ToList();

        // Whoever is owed the most comes first: that is the order the owner would ask for.
        _rows = _rows
            .OrderByDescending(s => s.Owed)
            .ThenBy(s => s.Name)
            .ToList();

        // Land on whoever is owed the most rather than on an empty panel. If a supplier was
        // already picked, keep them — unless a filter has just taken them off the list.
        if (_rows.All(s => s.Id != _picked)) _picked = _rows.Count > 0 ? _rows[0].Id : 0;

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;
        ShowEmptyState();
        FillSummary();
        ShowPicked();
    }

    // ============================== Summary ==============================

    /// <summary>
    /// The whole book, not the filtered list. What the shop owes does not change because
    /// somebody typed a name into the search box.
    /// </summary>
    private void FillSummary()
    {
        var all = SupplierRepository.List(includeInactive: true);

        var owed = all.Sum(s => s.Owed);
        var bought = all.Sum(s => s.TotalPurchased);
        var paid = all.Sum(s => s.TotalPaid);
        var owing = all.Count(s => s.Owed > 0m);
        var active = all.Count(s => s.IsActive);

        OwedValue.Text = Money(owed);
        OwedNote.Text = owing == 0
            ? Loc.T("nothing outstanding")
            : Loc.T(owing == 1 ? "to {0} supplier" : "to {0} suppliers", owing);

        BoughtValue.Text = Money(bought);
        BoughtNote.Text = "stock received, all time";

        PaidValue.Text = Money(paid);
        PaidNote.Text = bought <= 0m
            ? Loc.T("nothing paid yet")
            : Loc.T("{0}% of what was bought", Loc.Ltr($"{paid / bought * 100m:0}"));

        CountValue.Text = active.ToString();
        CountNote.Text = all.Count > active
            ? Loc.T("{0} no longer used", all.Count - active)
            : Loc.T(active == 0 ? "none added yet" : "on the books");
    }

    private static string Money(decimal amount) =>
        Loc.Ltr($"{amount:N2} {AppSettings.Current.Currency}");

    private void ShowEmptyState()
    {
        var filtered = SearchBox.Text.Trim().Length > 0 || OwedOnly.IsChecked == true;

        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_rows.Count > 0) return;

        EmptyTitle.Text = filtered ? "Nothing matches" : "No suppliers yet";
        EmptyBody.Text = filtered
            ? "Try a different name, or clear the filter."
            : "Add the wholesalers the shop buys from. Once a delivery is recorded against one, "
            + "what is owed to them shows up here.";
    }

    // ============================== The picked supplier ==============================

    private void ShowPicked()
    {
        var supplier = _rows.FirstOrDefault(s => s.Id == _picked);

        // The panel's own headings have to go with it — DELIVERIES and PAYMENTS over nothing
        // read as "there are none", which is a different statement from "pick a supplier".
        var picked = supplier is not null;
        NobodyPicked.Visibility = picked ? Visibility.Collapsed : Visibility.Visible;
        PickedHead.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        PickedBody.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;

        if (supplier is null) return;

        PickedName.Text = supplier.Name;
        PickedNote.Text = supplier.Owed > 0m
            ? Loc.T("{0} still owed of {1} bought.",
                    Money(supplier.Owed), Money(supplier.TotalPurchased))
            : supplier.TotalPurchased > 0m
                ? Loc.T("Paid up. {0} bought all told.", Money(supplier.TotalPurchased))
                : Loc.T("Nothing bought from them yet.");

        var goods = SupplierRepository.WhatWeBuy(supplier.Id);
        Goods.ItemsSource = goods;
        NoGoods.Visibility = goods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var purchases = SupplierRepository.ListPurchases(supplierId: supplier.Id);

        Deliveries.ItemsSource = purchases.Select(p => new Delivery
        {
            When = p.PurchasedOn.ToString("d MMM yyyy"),
            Cost = Money(p.Total),
            // "on this delivery", not just "owing": a payment made against the account rather
            // than against an invoice lowers what the supplier is owed without touching this
            // figure, and the two numbers then look like they contradict each other.
            Owing = p.Remaining > 0m
                ? Loc.T("{0} unpaid on this delivery", Money(p.Remaining))
                : Loc.T("settled"),
            StillOwing = p.Remaining > 0m,
            Lines = Contents(p),
        }).ToList();

        NoDeliveries.Visibility = purchases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var payments = SupplierRepository.ListPayments(supplierId: supplier.Id);
        Payments.ItemsSource = payments;
        NoPayments.Visibility = payments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// What was in the van: every product, how many, and at what each. A delivery summed to
    /// one figure cannot be checked against the paper invoice, which is the only reason
    /// anyone opens this in the first place.
    /// </summary>
    private static List<DeliveryLine> Contents(Purchase purchase)
    {
        var lines = SupplierRepository.ListPurchaseLines(purchase.Id);

        if (lines.Count == 0)
        {
            return
            [
                new DeliveryLine
                {
                    What = purchase.InvoiceNumber.Length > 0
                        ? Loc.T("Invoice {0} — no items listed", purchase.InvoiceNumber)
                        : Loc.T("No items listed"),
                    Cost = string.Empty,
                },
            ];
        }

        return lines.Select(line => new DeliveryLine
        {
            What = Loc.T("{0} × {1} at {2}", Loc.Ltr($"{line.Quantity:0.###}"),
                         line.Name, Loc.Ltr($"{line.UnitCost:N2}")),
            Cost = Money(line.LineTotal),
        }).ToList();
    }

    // ============================== Actions ==============================

    private Supplier? Row(object sender) =>
        sender is FrameworkElement { Tag: int id } ? _rows.FirstOrDefault(s => s.Id == id) : null;

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } supplier) return;

        _picked = supplier.Id;
        ShowPicked();
    }

    /// <summary>
    /// Adds a supplier, along with whatever they brought with them. The page then lands on
    /// them, so the delivery just entered is on screen without having to be looked for.
    /// </summary>
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || !SupplierWindow.AddNew(Shell, out var created)) return;

        _picked = created;
        ReloadAll();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || Row(sender) is not { } supplier) return;
        if (SupplierWindow.Edit(Shell, supplier)) ReloadAll();
    }

    /// <summary>A delivery: what arrived, what it cost, and how much was handed over there and then.</summary>
    private void Purchase_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null) return;
        if (PurchaseWindow.Show(Shell, _picked > 0 ? _picked : null)) ReloadAll();
    }

    /// <summary>
    /// Money paid against the balance. Suggested at the full amount owed, because that is
    /// what is normally handed over, and capped there — paying a supplier more than they are
    /// owed is a typo, not a transaction.
    /// </summary>
    private void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || Row(sender) is not { } supplier) return;

        var result = AmountWindow.Ask(Shell, new AmountRequest
        {
            Heading = $"Pay {supplier.Name}",
            Blurb = $"{Money(supplier.Owed)} outstanding.",
            AmountLabel = "AMOUNT PAID",
            ConfirmText = "Record payment",
            Suggested = supplier.Owed,
            Maximum = supplier.Owed,
        });

        if (result is null) return;

        SupplierRepository.Pay(supplier.Id, supplier.Name, result.Amount, result.Date,
                               result.Method, result.Note);
        ReloadAll();
    }
}
