using System.Windows;
using System.Windows.Media;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Every receipt the till has rung up, filterable, with the whole sale one click away.
///
/// Nothing on this page deletes anything. A sale rung up wrongly is refunded or cancelled,
/// and both leave the original transaction on record — that is the difference between a
/// shop's books and a list that can be tidied up afterwards.
///
/// The summary at the top reflects the filters, not just the date, so narrowing to one
/// cashier answers "what did they take" without any arithmetic.
/// </summary>
public partial class SalesHistoryPage : AdminPageBase
{
    private List<SaleSummaryEx> _rows = new();
    private bool _building;

    public SalesHistoryPage() => InitializeComponent();

    public override string Title => "Sales history";
    public override string Subtitle => "Every receipt, and what it made";
    public override bool UsesDateRange => true;

    protected override void Load()
    {
        FillCashiers();

        var seesMoney = Session.Can(Permission.SeeFinancials);
        ProfitHead.Visibility = seesMoney ? Visibility.Visible : Visibility.Hidden;

        _rows = SalesHistoryRepository.List(
            range: Dates.Range,
            search: SearchBox.Text,
            workerId: SelectedCashierId);

        Rows.ItemsSource = _rows;
        ShowEmptyState();
        FillSummary(seesMoney);
    }

    /// <summary>
    /// The staff you actually have, rebuilt each load so a newly added cashier appears without
    /// reopening the page.
    ///
    /// Only the people still working here, plus anyone who rang up a sale in the period being
    /// looked at — a cashier who left in March has to stay selectable while March is on
    /// screen, and has no business cluttering the list the rest of the year.
    /// </summary>
    private void FillCashiers()
    {
        _building = true;

        var selected = (CashierFilter.SelectedItem as Worker)?.Id ?? 0;

        var sold = SalesHistoryRepository.WhoSoldIn(Dates.Range);

        var cashiers = new List<Worker> { new() { Id = 0, Name = "Any cashier" } };
        cashiers.AddRange(WorkerRepository.List(includeInactive: true)
            .Where(w => w.IsActive || sold.Contains(w.Id)));

        CashierFilter.ItemsSource = cashiers;
        CashierFilter.SelectedItem = cashiers.FirstOrDefault(c => c.Id == selected) ?? cashiers[0];

        _building = false;
    }

    private int? SelectedCashierId =>
        (CashierFilter.SelectedItem as Worker)?.Id is > 0 and var id ? id : null;

    // ============================== Summary ==============================

    private void FillSummary(bool seesMoney)
    {
        // Cancelled sales stay in the list — the record matters — but they did not happen,
        // so they are kept out of every figure above it.
        var live = _rows.Where(r => !r.IsCancelled).ToList();

        var revenue = live.Sum(r => r.NetTotal);
        var cost = live.Sum(r => r.CostTotal);
        var refunded = _rows.Sum(r => r.Refunded);
        var items = live.Sum(r => r.LineCount);
        var costMissing = revenue > 0m && cost <= 0m;

        CountValue.Text = live.Count.ToString();
        CountNote.Text = FilterNote(live.Count);

        TakingsValue.Text = Money(revenue);
        TakingsNote.Text = live.Count == 0
            ? Loc.T("Nothing in this period")
            : Loc.T("{0} average sale", Loc.Ltr($"{revenue / live.Count:N0} DH"));

        if (!seesMoney)
        {
            ProfitValue.Text = "—";
            ProfitValue.Foreground = Brush("Brush.Muted");
            ProfitNote.Text = "Owner only";
        }
        else if (costMissing)
        {
            ProfitValue.Text = "—";
            ProfitValue.Foreground = Brush("Brush.Muted");
            ProfitNote.Text = "Purchase prices missing";
        }
        else
        {
            var profit = revenue - cost;
            ProfitValue.Text = Money(profit);
            ProfitValue.Foreground = Brush(profit < 0m ? "Brush.Danger" : "Brush.Accent");
            ProfitNote.Text = revenue <= 0m
                ? Loc.T("Nothing sold")
                : Loc.T("{0}% margin", Loc.Ltr($"{profit / revenue * 100m:0.#}"));
        }

        ItemsValue.Text = items.ToString();
        ItemsNote.Text = "lines on the receipts";

        RefundValue.Text = Money(refunded);
        var affected = _rows.Count(r => r.Status != SaleStatus.Completed);
        RefundNote.Text = affected == 0
            ? Loc.T("No returns")
            : Loc.T(affected == 1 ? "{0} sale affected" : "{0} sales affected", affected);
    }

    /// <summary>Says which filters are narrowing the list, so a small number is never a mystery.</summary>
    private string FilterNote(int shown)
    {
        var parts = new List<string>();
        if (SelectedCashierId is not null && CashierFilter.SelectedItem is Worker w) parts.Add(w.Name);
        if (SearchBox.Text.Trim().Length > 0) parts.Add($"\"{SearchBox.Text.Trim()}\"");

        if (parts.Count > 0) return Loc.T("filtered by {0}", string.Join(", ", parts));
        return Loc.T(shown == 0 ? "No sales yet" : "completed in this period");
    }

    private void ShowEmptyState()
    {
        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var filtered = SearchBox.Text.Trim().Length > 0 || SelectedCashierId is not null;

        EmptyTitle.Text = filtered ? "Nothing matches" : "No sales in this period";
        EmptyBody.Text = filtered
            ? "Try a different search, or set the cashier back to Any."
            : "Pick a wider date range above, or ring up a sale on the till.";
    }

    // ============================== Actions ==============================

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_building && IsLoaded) Refresh();
    }

    /// <summary>
    /// Opens the receipt: what was on it, what it made, and the two ways to undo it. Reloading
    /// afterwards keeps the list and the summary honest if a refund was taken.
    /// </summary>
    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || sender is not FrameworkElement { Tag: int invoice }) return;
        if (SaleDetailWindow.Show(Shell, invoice)) { Catalog.Reload(); ReloadAll(); }
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        Export.Announce(Shell, Export.Sales(_rows));

    // ============================== Helpers ==============================

    private static string Money(decimal value) => Loc.Ltr($"{value:N2} DH");

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private Brush Brush(string key) => (Brush)FindResource(key);
}
