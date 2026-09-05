using System.Windows;
using System.Windows.Media;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>One line of the What's selling table.</summary>
public sealed class SoldRow
{
    public required string Name { get; init; }
    public decimal Quantity { get; init; }
    public decimal Revenue { get; init; }

    /// <summary>What those units cost the shop to buy, frozen at the time of each sale.</summary>
    public decimal Cost { get; init; }

    /// <summary>
    /// How this product's units compare with the best seller's, 0 to 1. Drawn as a bar under
    /// the name: the figures say which sold most, the bar says by how much.
    /// </summary>
    public double Share { get; init; }

    /// <summary>
    /// A dash rather than 0.00 when the product has no purchase price: nothing on earth is
    /// free, so a zero here would be a lie the owner might price against.
    /// </summary>
    public string CostLabel => Cost <= 0m ? "—" : Services.Loc.Ltr($"{Cost:N2} DH");
}

/// <summary>One line of the Running out table.</summary>
public sealed class RestockRow
{
    /// <summary>Product id, so the row can open the product it is about.</summary>
    public int Id { get; init; }

    public required string Name { get; init; }
    public required string Left { get; init; }
    public required string Minimum { get; init; }
    public string Supplier { get; init; } = string.Empty;
    public required string Status { get; init; }
    public bool IsOut { get; init; }
}

/// <summary>
/// The screen a shop owner opens in the morning.
///
/// It answers one question loudly — what did the shop take — and everything else quietly
/// around it. Six figures at equal weight is six figures nobody reads, so takings carries the
/// page, profit stands beside it as the only number that can contradict it, and the rest sit
/// in a single line underneath.
///
/// Everything follows the date filter, and every figure is read live through
/// <see cref="Finance"/> and the repositories — so the dashboard can never drift away from the
/// pages behind it.
/// </summary>
public partial class DashboardPage : AdminPageBase
{
    /// <summary>
    /// A ceiling, not a page size. Both lists scroll inside their card, so the only thing this
    /// stops is a thousand-line ItemsControl being built to show ten rows.
    /// </summary>
    private const int MostRows = 40;

    public DashboardPage() => InitializeComponent();

    public override string Title => "Dashboard";
    public override string Subtitle => "How the shop is doing";
    public override bool UsesDateRange => true;

    protected override void Load()
    {
        var range = Dates.Range;
        var money = Finance.For(range);
        var sold = SalesHistoryRepository.ProductPerformance(range);

        FillSummary(range, money);
        FillChart(range);
        FillMostSold(sold);
        FillRestock();
    }

    // ========================= The hero and the quiet line =========================

    private void FillSummary(DateRange range, Financials money)
    {
        var costMissing = money.Revenue > 0m && money.Cogs <= 0m;

        // The headline figure has to say which period it is about, or "12,400 DH" is a fact
        // without a question attached to it.
        HeroLabel.Text = Loc.T("TAKINGS · {0}", Loc.T(range.Label).ToUpperInvariant());

        RevenueValue.Text = Money(money.Revenue);
        RevenueNote.Text = money.SaleCount == 0
            ? Loc.T("Nothing sold yet in this period")
            : Loc.T(money.SaleCount == 1 ? "{0} sale · {1} DH a basket"
                                         : "{0} sales · {1} DH a basket",
                    money.SaleCount, $"{money.AverageBasket:N0}");

        // Profit is the one figure whose sign changes the reading, so it carries the colour.
        if (costMissing)
        {
            ProfitValue.Text = "—";
            ProfitValue.Foreground = Brush("Brush.Muted");
            ProfitNote.Text = Loc.T("purchase prices missing");
        }
        else
        {
            ProfitValue.Text = Money(money.NetProfit);
            ProfitValue.Foreground = Brush(money.NetProfit < 0m ? "Brush.Danger" : "Brush.Accent");
            // Net profit above, so the note has to describe net profit. It said gross margin,
            // which is a different and always kinder number sitting under a red figure.
            ProfitNote.Text = money.Revenue <= 0m
                ? Loc.T("nothing sold yet")
                : money.NetProfit < 0m && -money.NetProfit > money.Revenue
                    ? Loc.T("the bills came to more than the {0} taken", Money(money.Revenue))
                    : Loc.T("{0}% of what was taken", Loc.Ltr($"{money.NetMarginPercent:0.#}"));
        }

        // ---- the quiet four ----

        // What the goods that sold actually cost to buy — the other half of the profit sum,
        // and the figure an owner checks a supplier's price against.
        if (costMissing)
        {
            CostValue.Text = "—";
            CostValue.Foreground = Brush("Brush.Muted");
            CostNote.Text = Loc.T("purchase prices missing");
        }
        else
        {
            CostValue.Text = Money(money.Cogs);
            CostValue.Foreground = Brush("Brush.Text");
            CostNote.Text = money.Revenue <= 0m
                ? Loc.T("nothing sold")
                : Loc.T("{0}% of what you charged", Loc.Ltr($"{money.Cogs / money.Revenue * 100m:0.#}"));
        }

        ExpensesValue.Text = Money(money.OperatingExpenses + money.SalaryExpense);
        ExpensesNote.Text = money.SalaryExpense > 0m
            ? Loc.T("includes {0} DH of wages", $"{money.SalaryExpense:N0}")
            : Loc.T("rent, power, water, wifi");

        SoldValue.Text = $"{money.ItemsSold:0.###}";
        SoldNote.Text = Loc.T(money.ItemsSold <= 0m ? "nothing left the shelf" : "items sold");

        var low = StockRepository.LowStock().Count;
        var outOf = StockRepository.OutOfStock().Count;

        RestockValue.Text = (low + outOf).ToString();
        RestockNote.Text = outOf > 0
            ? Loc.T("{0} of them at zero", outOf)
            : Loc.T(low > 0 ? "running low" : "everything stocked");

        ShowCostWarning(costMissing);
    }

    /// <summary>
    /// Without purchase prices every sale looks like pure profit, so profit is withheld rather
    /// than shown wrong. The note is grey, like every other secondary line: it is a setup task,
    /// not an emergency, and a red banner across the top made the whole page look alarmed.
    /// </summary>
    private void ShowCostWarning(bool costMissing)
    {
        if (!costMissing)
        {
            CostWarning.Visibility = Visibility.Collapsed;
            return;
        }

        var missing = StockRepository.List().Count(p => p.Cost <= 0m);
        CostWarning.Visibility = Visibility.Visible;
        CostWarningText.Text = Loc.T(
            "{0} products have no purchase price, so profit cannot be worked out yet. "
          + "Add cost prices under Inventory.", missing);
    }

    // ============================== Chart ==============================

    /// <summary>
    /// The shape of trade beside the headline figure, so a day's takings can be read as good
    /// or bad rather than merely large.
    ///
    /// The chosen period is drawn when it has more than one bucket to draw. On the default
    /// Today view it does not — one bar over an empty grid was the least honest thing on the
    /// old page — so the last fortnight stands in, which is the comparison the owner is making
    /// in their head anyway.
    /// </summary>
    private void FillChart(DateRange range)
    {
        var points = Trim(Finance.Series(range, SeriesKind.Revenue));
        var ownPeriod = points.Count > 1;

        if (!ownPeriod)
        {
            range = DateRange.Custom(DateTime.Today.AddDays(-13), DateTime.Today);
            points = Trim(Finance.Series(range, SeriesKind.Revenue));
        }

        // Nothing to draw is nothing to draw. A flat line along the bottom says less than the
        // "nothing sold yet" already sitting under the headline figure.
        if (points.Count < 2 || points.All(p => p.Value <= 0m))
        {
            SparkPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SparkPanel.Visibility = Visibility.Visible;
        Chart.Points = points;

        SparkLabel.Text = ownPeriod
            ? Loc.T(range.ByMonth ? "{0}, MONTH BY MONTH" : "{0}, DAY BY DAY",
                    Loc.T(range.Label).ToUpperInvariant())
            : Loc.T("THE LAST FORTNIGHT");

        var best = points.OrderByDescending(p => p.Value).First();
        SparkNote.Text = Loc.T("best {0}, {1}", best.Label, Money(best.Value));
    }

    /// <summary>
    /// Drops the days that have not happened yet. "This month" runs to the last of the month,
    /// so on the fourth the line ran flat along the bottom for twenty-six days and read as a
    /// business that had stopped trading.
    /// </summary>
    private static List<Finance.Point> Trim(List<Finance.Point> points) =>
        points.Where(p => p.At.Date <= DateTime.Today).ToList();

    // ============================== Tables ==============================

    private void FillMostSold(List<SalesHistoryRepository.ProductStat> sold)
    {
        // Ranked by units, not by revenue: what is selling is a question about how many left
        // the shelf, and one expensive item would otherwise outrank a hundred loaves.
        var top = sold.OrderByDescending(s => s.Quantity).Take(MostRows).ToList();
        var best = top.Count == 0 ? 0m : top.Max(s => s.Quantity);

        MostSold.ItemsSource = top.Select(s => new SoldRow
        {
            Name = s.Name,
            Quantity = s.Quantity,
            Cost = s.Cost,
            Revenue = s.Revenue,
            Share = best <= 0m ? 0 : (double)(s.Quantity / best),
        }).ToList();

        SellingNote.Text = top.Count == 0 ? string.Empty : Loc.T("by units sold");

        // What the listed rows add up to against the day's whole takings. On a short list it
        // closes the card; on a long one it says how much of the trade the top few carry,
        // which is the number behind "should I bother stocking the rest".
        var listed = top.Sum(t => t.Revenue);
        var all = sold.Sum(t => t.Revenue);
        SellingFooter.Visibility = top.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SellingFooterText.Text = sold.Count > top.Count
            ? Loc.T("these {0} brought in {1} of the {2} taken", top.Count, Money(listed), Money(all))
            : Loc.T(sold.Count == 1 ? "{0} product sold, {1} in all"
                                    : "{0} products sold, {1} in all", sold.Count, Money(all));

        MostSoldEmpty.Visibility = top.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Two different empty rooms. A shop with no products has not been set up; a shop with
        // products and no sales has simply not sold anything yet, and telling it to go and add
        // products would be nonsense.
        var stocked = StockRepository.List().Count > 0;
        MostSoldEmptyTitle.Text = Loc.T(stocked ? "No sales in this period" : "Nothing to sell yet");
        MostSoldEmptyBody.Text = Loc.T(stocked
            ? "Change the period above, or ring something up at the till — the best sellers appear here."
            : "Put what the shop sells in under Add product, and every sale will be counted here.");
    }

    private void FillRestock()
    {
        // Empty shelves first, then whatever is closest to running out.
        var all = StockRepository.List()
            .Where(p => p.Status is StockStatus.OutOfStock or StockStatus.LowStock)
            .OrderBy(p => p.Status == StockStatus.OutOfStock ? 0 : 1)
            .ThenBy(p => p.Stock)
            .ToList();

        Restock.ItemsSource = all.Take(MostRows).Select(p => new RestockRow
        {
            Id = p.Id,
            Name = p.Name,
            Left = $"{p.Stock:0.###}",
            Minimum = $"{p.MinStock:0.###}",
            Supplier = p.SupplierName,
            Status = Loc.T(p.Status == StockStatus.OutOfStock ? "Out" : "Low"),
            IsOut = p.Status == StockStatus.OutOfStock,
        }).ToList();

        RestockEmpty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestockCount.Text = all.Count == 0
            ? string.Empty
            : Loc.T(all.Count == 1 ? "{0} product" : "{0} products", all.Count);

        var anyStock = StockRepository.List().Count > 0;
        RestockEmptyTitle.Text = Loc.T(anyStock ? "Nothing to reorder" : "No products yet");
        RestockEmptyBody.Text = Loc.T(anyStock
            ? "A product appears here once it drops to the smallest amount you set for it."
            : "Set a smallest amount on a product and it will warn you here before it runs out.");

        // Always offered, not only when the list overflows: the answer to a shelf running out
        // lives on the inventory page, and making the owner find it in the sidebar is a step
        // for nothing.
        var hidden = all.Count - MostRows;
        ViewAll.Visibility = anyStock ? Visibility.Visible : Visibility.Collapsed;
        ViewAll.Content = hidden > 0 ? Loc.T("View all {0}", all.Count) : Loc.T("Open the inventory");
    }

    /// <summary>
    /// Opens the product a restock row is about. Reloading afterwards means that if the owner
    /// sets a cost price or a new minimum while they are in there, the dashboard behind them
    /// is already telling the truth when the dialog closes.
    /// </summary>
    private void Restock_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || sender is not FrameworkElement { Tag: int id }) return;

        var product = StockRepository.Find(id);
        if (product is null) return;

        if (ProductWindow.Edit(Shell, product)) ReloadAll();
    }

    private void ViewAll_Click(object sender, RoutedEventArgs e) => Shell?.GoTo(AdminPage.Inventory);

    // ============================== Helpers ==============================

    private static string Money(decimal value) => Loc.Ltr($"{value:N2} DH");

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private Brush Brush(string key) => (Brush)FindResource(key);
}
