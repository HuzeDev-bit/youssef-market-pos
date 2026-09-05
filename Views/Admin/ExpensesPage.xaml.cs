using System.Windows;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// The bills: rent, electricity, water, internet — everything the shop pays that is not stock.
///
/// This is the side of the accounts a shopkeeper carries in their head and gets wrong. Stock
/// is visible on the shelf; the light bill is not, and a shop can look profitable all month
/// and still not cover its rent. So the page leads with what has been spent, what the biggest
/// bill is, and how much of it comes back every single month whether the shop sells anything
/// or not.
///
/// Buying stock is deliberately not here. Money leaves and goods arrive, so the shop is no
/// poorer for it — that cost lands in profit later, as cost of goods, when the item sells.
/// Counting a delivery as an expense would charge the shop twice for the same tin.
/// </summary>
public partial class ExpensesPage : AdminPageBase
{
    /// <summary>One kind of bill and what it came to, sized for the bar beside it.</summary>
    private sealed class KindBar
    {
        public required string Kind { get; init; }
        public decimal Amount { get; init; }
        public decimal Share { get; init; }
        public double BarWidth { get; set; }

        public string AmountLabel => Loc.Ltr($"{Amount:N2} {AppSettings.Current.Currency}");
        public string ShareLabel => $"{Share * 100m:0}% of what was spent";
    }

    /// <summary>How wide the biggest bar is drawn. Everything else is a share of it.</summary>
    private const double FullBar = 300;

    private List<Expense> _rows = new();
    private bool _building;

    public ExpensesPage() => InitializeComponent();

    public override string Title => "Expenses";
    public override string Subtitle => "Rent, light, water, internet — everything that is not stock";
    public override bool UsesDateRange => true;

    protected override void Load()
    {
        Session.Require(Permission.ManageExpenses);

        FillKindFilter();

        _rows = ExpenseRepository.List(range: Dates.Range, categoryId: SelectedKindId);

        var search = SearchBox.Text.Trim();
        if (search.Length > 0)
        {
            _rows = _rows
                .Where(e => e.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                         || e.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                         || e.Note.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        }

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;

        ShowEmptyState();
        FillByKind();
        FillSummary();
    }

    // ============================== Filter ==============================

    private void FillKindFilter()
    {
        if (KindFilter.ItemsSource is not null) return;

        _building = true;

        var kinds = new List<string> { Loc.T("Every kind") };
        kinds.AddRange(ExpenseRepository.Categories().Select(c => c.Name));

        KindFilter.ItemsSource = kinds;
        KindFilter.SelectedIndex = 0;

        _building = false;
    }

    private int? SelectedKindId
    {
        get
        {
            if (KindFilter.SelectedIndex <= 0) return null;

            var name = KindFilter.SelectedItem as string;
            return ExpenseRepository.Categories()
                .FirstOrDefault(c => c.Name == name) is { Id: > 0 } found ? found.Id : null;
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !_building) Refresh();
    }

    // ============================== Where it goes ==============================

    private void FillByKind()
    {
        var kinds = ExpenseRepository.ByCategory(Dates.Range)
            .Where(k => k.Amount > 0m)
            .ToList();

        var total = kinds.Sum(k => k.Amount);
        var biggest = kinds.Count == 0 ? 0m : kinds.Max(k => k.Amount);

        var bars = kinds.Select(k => new KindBar
        {
            Kind = k.Category,
            Amount = Math.Round(k.Amount, 2),
            Share = total <= 0m ? 0m : k.Amount / total,
            // Against the biggest, not the total: the point of the chart is which bill is
            // costing the most, and shares of a total flatten that when there are eight.
            BarWidth = biggest <= 0m ? 0 : (double)(k.Amount / biggest) * FullBar,
        }).ToList();

        ByKind.ItemsSource = bars;
        NoKinds.Visibility = bars.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ByKindNote.Text = bars.Count == 0
            ? string.Empty
            : Loc.T(bars.Count == 1 ? "{0} kind of bill, {1} in all."
                                    : "{0} kinds of bill, {1} in all.",
                    bars.Count, Money(total));
    }

    // ============================== Summary ==============================

    /// <summary>
    /// Cancelled bills are on the list but out of every figure — they did not happen. The
    /// repository already excludes them from its totals; the rows are filtered here to match,
    /// or the page would disagree with itself.
    /// </summary>
    private void FillSummary()
    {
        var live = _rows.Where(e => !e.IsVoid).ToList();
        var total = ExpenseRepository.Total(Dates.Range);

        TotalValue.Text = Money(total);
        TotalNote.Text = live.Count == 0
            ? Loc.T("nothing recorded")
            : Loc.T(live.Count == 1 ? "across {0} bill · {1}" : "across {0} bills · {1}",
                    live.Count, Loc.T(Dates.RangeLabel).ToLowerInvariant());

        var kinds = ExpenseRepository.ByCategory(Dates.Range)
            .Where(k => k.Amount > 0m)
            .OrderByDescending(k => k.Amount)
            .ToList();

        if (kinds.Count == 0)
        {
            BiggestValue.Text = "—";
            BiggestNote.Text = Loc.T("nothing spent in this period");
        }
        else
        {
            BiggestValue.Text = kinds[0].Category;
            BiggestNote.Text = total <= 0m
                ? Money(kinds[0].Amount)
                : Loc.T("{0} · {1}% of the total",
                        Money(kinds[0].Amount), Loc.Ltr($"{kinds[0].Amount / total * 100m:0}"));
        }

        // What comes back every month whether the shop sells anything or not. This is the
        // number that says how much has to be taken before the doors have paid for themselves.
        var fixedBills = ExpenseRepository
            .List(range: Dates.Range)
            .Where(e => !e.IsVoid && e.Recurring == Recurrence.Monthly)
            .ToList();

        var fixedTotal = fixedBills.Sum(e => e.Amount);

        FixedValue.Text = Money(fixedTotal);
        FixedNote.Text = fixedBills.Count == 0
            ? Loc.T("no monthly bills marked yet")
            : Loc.T(fixedBills.Count == 1 ? "{0} bill that comes back"
                                          : "{0} bills that come back", fixedBills.Count);

        // Against revenue, because that is what says whether it matters. Two thousand dirhams
        // of bills is nothing in a busy month and frightening in a quiet one.
        var revenue = Finance.For(Dates.Range).Revenue;
        if (revenue <= 0m)
        {
            ShareValue.Text = "—";
            ShareNote.Text = Loc.T("no sales in this period");
        }
        else if (total > revenue)
        {
            // A quiet period puts this in the thousands of percent, which is a number nobody
            // can read. What matters at that point is simply that the bills were bigger.
            ShareValue.Text = Loc.T("Over 100%");
            ShareNote.Text = Loc.T("{0} of bills on {1} taken", Money(total), Money(revenue));
        }
        else
        {
            ShareValue.Text = Loc.Ltr($"{total / revenue * 100m:0.0}%");
            ShareNote.Text = Loc.T("of {0} taken", Money(revenue));
        }
    }

    private static string Money(decimal amount) =>
        Loc.Ltr($"{amount:N2} {AppSettings.Current.Currency}");

    private void ShowEmptyState()
    {
        var filtered = SearchBox.Text.Trim().Length > 0 || KindFilter.SelectedIndex > 0;

        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_rows.Count > 0) return;

        EmptyTitle.Text = filtered ? "Nothing matches" : "No bills recorded";
        EmptyBody.Text = filtered
            ? "Try a different search, or another kind."
            : "Put in the rent, the light, the water and the internet. Mark the ones that come "
            + "back every month and the shop will know what it has to take before it makes anything.";
    }

    // ============================== Actions ==============================

    private Expense? Row(object sender) =>
        sender is FrameworkElement { Tag: int id } ? _rows.FirstOrDefault(e => e.Id == id) : null;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is not null && ExpenseWindow.AddNew(Shell)) ReloadAll();
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || Row(sender) is not { } expense) return;
        if (ExpenseWindow.Edit(Shell, expense)) ReloadAll();
    }

    /// <summary>
    /// Copies a monthly bill into this month. Opened rather than saved outright, because bills
    /// change — the electricity is never the same twice, and a figure carried over unchecked is
    /// worse than no figure at all.
    /// </summary>
    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || Row(sender) is not { } expense) return;
        if (ExpenseWindow.Repeat(Shell, expense)) ReloadAll();
    }
}
