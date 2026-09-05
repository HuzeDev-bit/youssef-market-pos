using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>The profit picture for one window of time.</summary>
public sealed class Financials
{
    public required DateRange Range { get; init; }

    public decimal Revenue { get; init; }
    public decimal Cogs { get; init; }
    public decimal GrossProfit => Revenue - Cogs;

    public decimal OperatingExpenses { get; init; }
    public decimal SalaryExpense { get; init; }
    public decimal StockLosses { get; init; }

    /// <summary>Everything that reduces profit but is not the cost of what was sold.</summary>
    public decimal TotalOperating => OperatingExpenses + SalaryExpense + StockLosses;

    public decimal NetProfit => GrossProfit - TotalOperating;

    public decimal GrossMarginPercent => Revenue <= 0m ? 0m : Math.Round(GrossProfit / Revenue * 100m, 1);
    public decimal NetMarginPercent => Revenue <= 0m ? 0m : Math.Round(NetProfit / Revenue * 100m, 1);

    // Cash side, which is a different question from profit.
    public decimal CashCollected { get; init; }
    public decimal CardCollected { get; init; }
    public decimal SupplierPayments { get; init; }
    public decimal StockPurchased { get; init; }

    /// <summary>
    /// Money that actually left the business. Stock bought on credit is NOT here — only the
    /// supplier payments that settled it — which is what keeps this from double-counting
    /// against <see cref="Cogs"/>.
    /// </summary>
    public decimal MoneySpent => SupplierPayments + OperatingExpenses + SalaryExpense;

    public int SaleCount { get; init; }
    public decimal ItemsSold { get; init; }
    public decimal Discounts { get; init; }
    public decimal Refunds { get; init; }

    public decimal AverageBasket => SaleCount == 0 ? 0m : Math.Round(Revenue / SaleCount, 2);
}

/// <summary>
/// The one definition of revenue, cost and profit in the application.
///
/// Every page that shows a money figure asks this class for it, so the dashboard, the reports
/// and the export cannot answer the same question three different ways. The accounting is
/// deliberately plain:
///
///     Revenue      = completed sales, less anything refunded
///     COGS         = the cost snapshotted onto those sale lines when they were rung up
///     Gross profit = Revenue − COGS
///     Net profit   = Gross profit − operating expenses − salaries paid − stock written off
///
/// Buying stock never appears as an expense. It becomes COGS when the item sells, and the
/// cash side of it shows up as a supplier payment under <see cref="Financials.MoneySpent"/>.
/// </summary>
public static class Finance
{
    public static Financials For(DateRange range)
    {
        var (revenue, cogs, saleCount, items, discounts, refunds, cash, card) = SalesSide(range);

        return new Financials
        {
            Range = range,
            Revenue = revenue,
            Cogs = cogs,
            SaleCount = saleCount,
            ItemsSold = items,
            Discounts = discounts,
            Refunds = refunds,
            CashCollected = cash,
            CardCollected = card,
            OperatingExpenses = ExpenseRepository.Total(range),
            SalaryExpense = WorkerRepository.PaidIn(range),
            StockLosses = InventoryRepository.LossesByReason(range).Sum(l => l.Value),
            SupplierPayments = SupplierPaymentsIn(range),
            StockPurchased = StockPurchasedIn(range),
        };
    }

    /// <summary>
    /// Sales, cost and takings in one pass.
    ///
    /// Cancelled sales are excluded outright. Refunds are subtracted from revenue rather than
    /// hidden, and the matching cost is subtracted too — a returned item is back on the shelf,
    /// so its cost is no longer a cost of goods sold.
    /// </summary>
    private static (decimal Revenue, decimal Cogs, int Count, decimal Items,
                    decimal Discounts, decimal Refunds, decimal Cash, decimal Card)
        SalesSide(DateRange range)
    {
        using var connection = Database.Open();

        decimal revenue = 0m, refunds = 0m, discounts = 0m, cash = 0m, card = 0m;
        int count = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT COUNT(*),
                       {Db.Sum("total")},
                       {Db.Sum("refunded")},
                       {Db.Sum("discount_amount")},
                       COALESCE(SUM(CASE WHEN payment_method = 'Cash'
                                    THEN CAST(total AS REAL) - CAST(refunded AS REAL) ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN payment_method = 'Card'
                                    THEN CAST(total AS REAL) - CAST(refunded AS REAL) ELSE 0 END), 0)
                FROM sales
                WHERE is_voided = 0 AND status <> 'Cancelled'
                  AND sold_at >= $from AND sold_at < $to;
                """;
            command.WithDate("$from", range.From).WithDate("$to", range.To);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                count = reader.GetInt32(0);
                revenue = (decimal)reader.GetDouble(1);
                refunds = (decimal)reader.GetDouble(2);
                discounts = (decimal)reader.GetDouble(3);
                cash = (decimal)reader.GetDouble(4);
                card = (decimal)reader.GetDouble(5);
            }
        }

        decimal cogs = 0m, itemsSold = 0m;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT COALESCE(SUM((CAST(l.quantity AS REAL) - CAST(l.returned_qty AS REAL))
                                    * CAST(l.unit_cost AS REAL)), 0),
                       COALESCE(SUM(CAST(l.quantity AS REAL) - CAST(l.returned_qty AS REAL)), 0)
                FROM sale_lines l JOIN sales s ON s.id = l.sale_id
                WHERE s.is_voided = 0 AND s.status <> 'Cancelled'
                  AND s.sold_at >= $from AND s.sold_at < $to;
                """;
            command.WithDate("$from", range.From).WithDate("$to", range.To);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                cogs = (decimal)reader.GetDouble(0);
                itemsSold = (decimal)reader.GetDouble(1);
            }
        }

        return (revenue - refunds, cogs, count, itemsSold, discounts, refunds, cash, card);
    }

    private static decimal SupplierPaymentsIn(DateRange range)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Db.Sum("amount")} FROM supplier_payments
            WHERE paid_on >= $from AND paid_on < $to;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To);
        return (decimal)Convert.ToDouble(command.ExecuteScalar());
    }

    /// <summary>Value of stock that arrived in the period, paid for or not. Not an expense — an asset.</summary>
    private static decimal StockPurchasedIn(DateRange range)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Db.Sum("total")} FROM purchases
            WHERE status = 'Received' AND purchased_on >= $from AND purchased_on < $to;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To);
        return (decimal)Convert.ToDouble(command.ExecuteScalar());
    }

    // ------------------------------ Money spent ------------------------------

    public sealed record SpendLine(string Name, decimal Amount, string Explanation = "");

    /// <summary>
    /// Where the money actually went in the period, each dirham appearing exactly once.
    ///
    /// Stock deliveries are shown separately and marked, because they are the figure owners
    /// most often expect to see here — but they are not added into the total, since the cash
    /// only leaves on a supplier payment.
    /// </summary>
    public static (List<SpendLine> Lines, decimal Total, decimal StockReceived) MoneySpent(DateRange range)
    {
        var lines = new List<SpendLine>
        {
            new("Supplier payments", SupplierPaymentsIn(range),
                "Money handed to suppliers, whenever the goods arrived."),
            new("Worker salaries", WorkerRepository.PaidIn(range),
                "Salary payments recorded in this period."),
        };

        lines.AddRange(ExpenseRepository.ByCategory(range)
            .Select(e => new SpendLine(e.Category, e.Amount)));

        var total = lines.Sum(l => l.Amount);
        return (lines.Where(l => l.Amount > 0m).OrderByDescending(l => l.Amount).ToList(),
                total, StockPurchasedIn(range));
    }

    // -------------------------------- Series --------------------------------

    public sealed record Point(DateTime At, string Label, decimal Value);

    /// <summary>
    /// A value per day (or per month for long ranges), with empty buckets included so a chart
    /// shows the quiet days rather than closing the gap and implying trade that never happened.
    /// </summary>
    public static List<Point> Series(DateRange range, SeriesKind kind)
    {
        var buckets = Buckets(range);
        var totals = new Dictionary<DateTime, decimal>();

        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var byMonth = range.ByMonth;
        var bucketExpr = byMonth ? "substr({0}, 1, 7)" : "substr({0}, 1, 10)";

        command.CommandText = kind switch
        {
            SeriesKind.Revenue => $"""
                SELECT {string.Format(bucketExpr, "sold_at")}, {Db.Sum("total")} - {Db.Sum("refunded")}
                FROM sales WHERE is_voided = 0 AND status <> 'Cancelled'
                  AND sold_at >= $from AND sold_at < $to GROUP BY 1;
                """,

            SeriesKind.Profit => $"""
                SELECT {string.Format(bucketExpr, "sold_at")},
                       {Db.Sum("total")} - {Db.Sum("refunded")} - {Db.Sum("cost_total")}
                FROM sales WHERE is_voided = 0 AND status <> 'Cancelled'
                  AND sold_at >= $from AND sold_at < $to GROUP BY 1;
                """,

            SeriesKind.Expenses => $"""
                SELECT {string.Format(bucketExpr, "spent_on")}, {Db.Sum("amount")}
                FROM expenses WHERE is_void = 0 AND spent_on >= $from AND spent_on < $to GROUP BY 1;
                """,

            _ => $"""
                SELECT {string.Format(bucketExpr, "sold_at")}, COUNT(*)
                FROM sales WHERE is_voided = 0 AND status <> 'Cancelled'
                  AND sold_at >= $from AND sold_at < $to GROUP BY 1;
                """,
        };
        command.WithDate("$from", range.From).WithDate("$to", range.To);

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var at = byMonth
                    ? DateTime.TryParse(key + "-01", out var m) ? m : DateTime.MinValue
                    : DateTime.TryParse(key, out var d) ? d : DateTime.MinValue;
                if (at != DateTime.MinValue)
                    totals[at] = (decimal)Convert.ToDouble(reader.GetValue(1));
            }
        }

        return buckets
            .Select(b => new Point(b, byMonth ? b.ToString("MMM") : b.ToString("d MMM"),
                                   totals.GetValueOrDefault(b)))
            .ToList();
    }

    private static List<DateTime> Buckets(DateRange range)
    {
        var buckets = new List<DateTime>();
        if (range.ByMonth)
        {
            var cursor = new DateTime(range.From.Year, range.From.Month, 1);
            while (cursor < range.To) { buckets.Add(cursor); cursor = cursor.AddMonths(1); }
        }
        else
        {
            var cursor = range.From.Date;
            while (cursor < range.To) { buckets.Add(cursor); cursor = cursor.AddDays(1); }
        }
        return buckets;
    }
}

public enum SeriesKind
{
    Revenue,
    Profit,
    Expenses,
    SaleCount,
}
