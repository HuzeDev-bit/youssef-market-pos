using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>
/// The alert centre. Nothing is stored — every alert is recomputed from live data, so an
/// alert cannot survive the thing that caused it. Restocking a product makes its low-stock
/// warning disappear on the next look, with no queue to clear.
///
/// Order matters: money that is overdue comes before stock that is merely low, because that
/// is the order an owner would want to be told about them.
/// </summary>
public static class Notifications
{
    /// <summary>How near expiry a product has to be before it is worth mentioning.</summary>
    private const int ExpiryWarningDays = 14;

    public static List<Alert> Build()
    {
        var alerts = new List<Alert>();

        try
        {
            AddSupplierAlerts(alerts);
            AddSalaryAlerts(alerts);
            AddStockAlerts(alerts);
        }
        catch
        {
            // The badge in the sidebar must never be the thing that crashes the office.
        }

        return alerts;
    }

    private static void AddSupplierAlerts(List<Alert> alerts)
    {
        if (!Session.Can(Permission.ManagePurchases)) return;

        var purchases = SupplierRepository.ListPurchases();

        foreach (var purchase in purchases.Where(p => p.IsOverdue).OrderBy(p => p.DueOn))
        {
            var days = (DateTime.Today - purchase.DueOn!.Value.Date).Days;
            alerts.Add(new Alert
            {
                Title = Loc.T("{0} — {1} overdue", purchase.SupplierName,
                              Loc.Ltr($"{purchase.Remaining:N2} DH")),
                Detail = Loc.T(days == 1 ? "Invoice {0} was due {1} day ago."
                                         : "Invoice {0} was due {1} days ago.",
                               purchase.InvoiceNumber.Length > 0
                                   ? purchase.InvoiceNumber : "#" + purchase.Id, days),
                Level = AlertLevel.Danger,
                GoTo = AdminPage.Suppliers,
            });
        }

        foreach (var purchase in purchases
                     .Where(p => p.DueOn is { } d && p.Remaining > 0m
                                 && d.Date >= DateTime.Today && d.Date <= DateTime.Today.AddDays(7))
                     .OrderBy(p => p.DueOn))
        {
            alerts.Add(new Alert
            {
                Title = Loc.T("{0} — {1} due {2}", purchase.SupplierName,
                              Loc.Ltr($"{purchase.Remaining:N2} DH"), Soon(purchase.DueOn!.Value)),
                Detail = Loc.T("Supplier payment coming up."),
                Level = AlertLevel.Warning,
                GoTo = AdminPage.Suppliers,
            });
        }
    }

    private static void AddSalaryAlerts(List<Alert> alerts)
    {
        if (!Session.Can(Permission.SeeSalaries)) return;

        var month = DateRange.For(DatePreset.ThisMonth);
        foreach (var row in WorkerRepository.Ledger(month)
                     .Where(l => l.Remaining > 0m && l.Worker.Salary > 0m)
                     .OrderByDescending(l => l.Remaining))
        {
            alerts.Add(new Alert
            {
                Title = Loc.T("{0} — {1} unpaid", row.Worker.Name,
                              Loc.Ltr($"{row.Remaining:N2} DH")),
                Detail = row.Status == PaymentStatus.PartiallyPaid
                    ? Loc.T("{0} of {1} paid this month.",
                            Loc.Ltr($"{row.Paid:N2} DH"), Loc.Ltr($"{row.Due:N2} DH"))
                    : Loc.T("No salary recorded for this month yet."),
                Level = AlertLevel.Warning,
                GoTo = AdminPage.Workers,
            });
        }
    }

    private static void AddStockAlerts(List<Alert> alerts)
    {
        var out_ = StockRepository.OutOfStock();
        if (out_.Count > 0)
        {
            alerts.Add(new Alert
            {
                Title = Loc.T(out_.Count == 1 ? "{0} product is out of stock"
                                            : "{0} products are out of stock", out_.Count),
                Detail = Join(out_.Select(p => p.Name)),
                Level = AlertLevel.Danger,
                GoTo = AdminPage.Inventory,
            });
        }

        var low = StockRepository.LowStock();
        if (low.Count > 0)
        {
            alerts.Add(new Alert
            {
                Title = Loc.T(low.Count == 1 ? "{0} product is running low"
                                           : "{0} products are running low", low.Count),
                Detail = Join(low.Select(p => Loc.T("{0} ({1} left)", p.Name,
                                                Loc.Ltr($"{p.Stock:0.###}")))),
                Level = AlertLevel.Warning,
                GoTo = AdminPage.Inventory,
            });
        }

        var expired = StockRepository.Expiring(0).Where(p => p.IsExpired).ToList();
        if (expired.Count > 0)
        {
            alerts.Add(new Alert
            {
                Title = Loc.T(expired.Count == 1 ? "{0} product has expired"
                                               : "{0} products have expired", expired.Count),
                Detail = Join(expired.Select(p => p.Name))
                       + Loc.T(" — take them off the shelf and record the loss."),
                Level = AlertLevel.Danger,
                GoTo = AdminPage.Inventory,
            });
        }

        var expiring = StockRepository.Expiring(ExpiryWarningDays).Where(p => !p.IsExpired).ToList();
        if (expiring.Count > 0)
        {
            alerts.Add(new Alert
            {
                Title = Loc.T(expiring.Count == 1 ? "{0} product expires within {1} days"
                                                : "{0} products expire within {1} days",
                              expiring.Count, ExpiryWarningDays),
                Detail = Join(expiring.Select(p => $"{p.Name} ({p.ExpiresOn:d MMM})")),
                Level = AlertLevel.Warning,
                GoTo = AdminPage.Inventory,
            });
        }
    }

    /// <summary>Names, capped, so an alert stays one readable line rather than a wall of stock.</summary>
    private static string Join(IEnumerable<string> names, int max = 4)
    {
        var list = names.ToList();
        return list.Count <= max
            ? string.Join(", ", list)
            : string.Join(", ", list.Take(max)) + $" and {list.Count - max} more";
    }

    private static string Soon(DateTime date) =>
        date.Date == DateTime.Today ? "today"
        : date.Date == DateTime.Today.AddDays(1) ? "tomorrow"
        : $"on {date:d MMM}";
}
