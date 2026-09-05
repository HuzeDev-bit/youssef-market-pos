using System.Windows;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// How the period went, and what needs doing about it.
///
/// One page rather than two. The alerts and the report answer the same question at two
/// distances — "did the shop make money" and "what is going wrong right now" — and on
/// separate screens the second one gets read once a week instead of once a day.
///
/// The statement is written to be read top to bottom, in shop language, because the point is
/// not to produce accounts. It is to answer the question an owner actually has: the till took
/// this much, so where did it go, and what is left.
/// </summary>
public partial class ReportsPage : AdminPageBase
{
    /// <summary>One line of the statement.</summary>
    private sealed class Line
    {
        public required string Label { get; init; }
        public required string Amount { get; init; }
        public string Detail { get; init; } = string.Empty;

        /// <summary>A line the ones above it add up to. Drawn heavier, with a rule over it.</summary>
        public bool IsTotal { get; init; }

        /// <summary>Starts a new question — the cash side, which is not part of the subtraction.</summary>
        public bool Breaks { get; init; }
    }

    /// <summary>An alert, with where it came from so the click can go there.</summary>
    private sealed class AlertRow
    {
        public required int Index { get; init; }
        public required string Title { get; init; }
        public string Detail { get; init; } = string.Empty;
        public required AlertLevel Level { get; init; }
    }

    private List<Alert> _alerts = new();

    public ReportsPage() => InitializeComponent();

    public override string Title => "Reports";
    public override string Subtitle => "What the shop made, and what needs doing";
    public override bool UsesDateRange => true;

    protected override void Load()
    {
        Session.Require(Permission.SeeReports);

        FillStatement();
        FillAlerts();
    }

    // ============================== The statement ==============================

    private void FillStatement()
    {
        var f = Finance.For(Dates.Range);

        RevenueValue.Text = Money(f.Revenue);
        RevenueNote.Text = f.Refunds > 0m
            ? Loc.T("after {0} refunded", Money(f.Refunds))
            : Loc.T(f.SaleCount == 0 ? "nothing sold yet" : "taken over the counter");

        GrossValue.Text = Money(f.GrossProfit);
        GrossNote.Text = f.Revenue <= 0m
            ? Loc.T("no sales to measure")
            : Loc.T("{0}% of what was taken", Loc.Ltr($"{f.GrossMarginPercent:0.#}"));

        NetValue.Text = Money(f.NetProfit);
        NetValue.Foreground = (System.Windows.Media.Brush)FindResource(
            f.NetProfit < 0m ? "Brush.Danger" : "Brush.Text");
        NetNote.Text = f.Revenue <= 0m
            ? Loc.T("after the bills")
            // A quiet period against a month of rent puts this in the thousands of percent,
            // which is a number nobody can read. What matters then is simply that it is under.
            : f.NetProfit < 0m && -f.NetProfit > f.Revenue
                ? Loc.T("the bills came to more than the {0} taken", Money(f.Revenue))
                : Loc.T("{0}% of what was taken", Loc.Ltr($"{f.NetMarginPercent:0.#}"));

        SalesValue.Text = f.SaleCount.ToString();
        SalesNote.Text = f.SaleCount == 0
            ? Loc.T("no sales in this period")
            : Loc.T("{0} items · {1} a basket",
                    Loc.Ltr($"{f.ItemsSold:0.###}"), Money(f.AverageBasket));

        PeriodNote.Text = $"{Loc.T(Dates.RangeLabel)} · {AppSettings.Current.BusinessName}";

        // Written as subtraction, in order, so the last figure is arrived at rather than
        // asserted. An owner who disagrees with net profit can see which line they dispute.
        var lines = new List<Line>
        {
            new()
            {
                Label = Loc.T("The till took"),
                Amount = Money(f.Revenue),
                Detail = f.Discounts > 0m
                    ? Loc.T("{0} sales, after {1} of discounts and {2} refunded",
                            f.SaleCount, Money(f.Discounts), Money(f.Refunds))
                    : Loc.T("{0} sales, after {1} refunded", f.SaleCount, Money(f.Refunds)),
            },
            new()
            {
                Label = Loc.T("− what those goods cost the shop"),
                Amount = Taken(f.Cogs),
                Detail = Loc.T("the price paid for exactly what was sold, frozen at the moment of sale"),
            },
            new()
            {
                Label = Loc.T("= gross profit"),
                IsTotal = true,
                Amount = Money(f.GrossProfit),
                Detail = f.Revenue <= 0m ? string.Empty
                    : Loc.T("{0}% of what was taken, before any bills",
                            Loc.Ltr($"{f.GrossMarginPercent:0.#}")),
            },
            new()
            {
                Label = Loc.T("− bills"),
                Amount = Taken(f.OperatingExpenses),
                Detail = Loc.T("rent, light, water, internet and the rest"),
            },
            new()
            {
                Label = Loc.T("− wages paid"),
                Amount = Taken(f.SalaryExpense),
                Detail = Loc.T("what actually went to staff in this period"),
            },
            new()
            {
                Label = Loc.T("− stock written off"),
                Amount = Taken(f.StockLosses),
                Detail = Loc.T("broken, expired, lost or used in the shop, at what it cost"),
            },
            new()
            {
                Label = Loc.T("= net profit"),
                IsTotal = true,
                Amount = Money(f.NetProfit),
                Detail = Loc.T(f.NetProfit < 0m
                    ? "the shop spent more than it made in this period"
                    : "what the shop actually kept"),
            },
            new()
            {
                Label = Loc.T("Money that left the shop"),
                Breaks = true,
                Amount = Money(f.MoneySpent),
                Detail = Loc.T("suppliers paid, bills and wages. Stock bought on credit is not "
                             + "here — only what was handed over."),
            },
            new()
            {
                Label = Loc.T("Stock that came in"),
                Amount = Money(f.StockPurchased),
                Detail = Loc.T("goods received. Not an expense: the shop swapped money for "
                             + "stock and is no poorer until it sells."),
            },
        };

        Statement.ItemsSource = lines;
    }

    private static string Money(decimal amount) =>
        Loc.Ltr($"{amount:N2} {AppSettings.Current.Currency}");

    /// <summary>
    /// A line that takes something off the one above it. Nothing taken away is written as
    /// nothing, not as "−0.00" — which is what a shop that has not traded yet was shown on
    /// every line of its own accounts on the first morning.
    /// </summary>
    private static string Taken(decimal amount) =>
        amount == 0m ? Money(0m) : "−" + Money(amount);

    // ============================== What needs doing ==============================

    private void FillAlerts()
    {
        _alerts = Notifications.Build();

        Alerts.ItemsSource = _alerts.Select((a, i) => new AlertRow
        {
            Index = i,
            Title = a.Title,
            Detail = a.Detail,
            Level = a.Level,
        }).ToList();

        AllClear.Visibility = _alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var urgent = _alerts.Count(a => a.Level == AlertLevel.Danger);
        AlertNote.Text = _alerts.Count == 0
            ? Loc.T("Nothing is overdue or running out.")
            : urgent > 0
                ? Loc.T("{0} to look at, {1} urgent.", _alerts.Count, urgent)
                : Loc.T("{0} to look at.", _alerts.Count);
    }

    /// <summary>Each alert knows the page that fixes it, so reading one is one click from acting.</summary>
    private void Alert_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || sender is not FrameworkElement { Tag: int index }) return;
        if (index < 0 || index >= _alerts.Count) return;

        if (_alerts[index].GoTo is { } page) Shell.GoTo(page);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        Session.Require(Permission.ExportData);
        Export.Announce(Shell, Export.ProfitReport(Dates.Range));
    }
}
