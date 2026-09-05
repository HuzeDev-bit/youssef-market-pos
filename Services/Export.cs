using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>
/// CSV export for the back office.
///
/// CSV rather than xlsx on purpose: it opens in Excel, in Google Sheets and in whatever the
/// shop's accountant uses, and writing it needs no library on a machine that may never see an
/// update. The file is written with a UTF-8 BOM because Excel on a French/Arabic Windows will
/// otherwise mangle every accented product name.
/// </summary>
public static class Export
{
    /// <summary>Where files go. The owner's Documents folder unless they picked somewhere else.</summary>
    private static string Folder
    {
        get
        {
            var configured = AppSettings.Current.ExportFolder;
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MarketPos")
                : configured;
            Directory.CreateDirectory(root);
            return root;
        }
    }

    // ------------------------------- Writers -------------------------------

    public static string Products(IReadOnlyList<StockItem> items) => Write("products",
        ["Name", "Barcode", "SKU", "Category", "Unit", "Cost", "Price", "Margin %", "Stock",
         "Min stock", "Stock value", "Supplier", "Shelf", "Expires", "Status", "In POS", "Active"],
        items.Select(i => new object?[]
        {
            i.Name, i.Barcode, i.Sku, i.Category, i.Unit, i.Cost, i.Price, i.MarginPercent,
            i.Stock, i.MinStock, i.StockValue, i.SupplierName, i.Shelf,
            i.ExpiresOn?.ToString("yyyy-MM-dd"), i.StatusLabel, i.ShowInPos ? "yes" : "no",
            i.IsActive ? "yes" : "no",
        }));

    public static string Sales(IReadOnlyList<SaleSummaryEx> sales) => Write("sales",
        ["Receipt", "Date", "Time", "Cashier", "Lines", "Discount", "Total", "Refunded",
         "Net", "Cost", "Profit", "Payment", "Status"],
        sales.Select(s => new object?[]
        {
            s.InvoiceNumber, s.SoldAt.ToString("yyyy-MM-dd"), s.SoldAt.ToString("HH:mm:ss"),
            s.CashierLabel, s.LineCount, s.DiscountAmount, s.Total, s.Refunded, s.NetTotal,
            s.CostTotal, s.Profit, s.PaymentMethod, s.StatusLabel,
        }));

    public static string StockMovements(IReadOnlyList<StockMovement> movements) => Write("stock-movements",
        ["Date", "Time", "Product", "Reason", "Change", "Before", "After", "Unit cost",
         "Value", "Reference", "Worker", "Note"],
        movements.Select(m => new object?[]
        {
            m.MovedAt.ToString("yyyy-MM-dd"), m.MovedAt.ToString("HH:mm:ss"), m.ProductName,
            m.ReasonLabel, m.Quantity, m.BeforeQty, m.AfterQty, m.UnitCost, m.Value,
            m.Reference, m.WorkerName, m.Note,
        }));

    public static string Expenses(IReadOnlyList<Expense> expenses) => Write("expenses",
        ["Date", "Name", "Category", "Amount", "Payment", "Recurring", "Note"],
        expenses.Select(e => new object?[]
        {
            e.SpentOn.ToString("yyyy-MM-dd"), e.Name, e.Category, e.Amount, e.Method,
            e.Recurring == Recurrence.None ? string.Empty : e.Recurring.ToString(), e.Note,
        }));

    public static string Purchases(IReadOnlyList<Purchase> purchases) => Write("supplier-purchases",
        ["Date", "Supplier", "Invoice", "Total", "Paid", "Remaining", "Status", "Due", "Payment", "Note"],
        purchases.Select(p => new object?[]
        {
            p.PurchasedOn.ToString("yyyy-MM-dd"), p.SupplierName, p.InvoiceNumber, p.Total,
            p.Paid, p.Remaining, p.PaymentStatus, p.DueOn?.ToString("yyyy-MM-dd"), p.Method, p.Note,
        }));

    public static string SupplierPayments(IReadOnlyList<SupplierPayment> payments) => Write("supplier-payments",
        ["Date", "Supplier", "Purchase", "Amount", "Method", "Note"],
        payments.Select(p => new object?[]
        {
            p.PaidOn.ToString("yyyy-MM-dd"), p.SupplierName, p.PurchaseId, p.Amount, p.Method, p.Note,
        }));

    public static string SalaryPayments(IReadOnlyList<SalaryPayment> payments) => Write("worker-payments",
        ["Paid on", "Worker", "Period start", "Period end", "Due", "Paid", "Method", "Note"],
        payments.Select(p => new object?[]
        {
            p.PaidOn.ToString("yyyy-MM-dd"), p.WorkerName, p.PeriodStart.ToString("yyyy-MM-dd"),
            p.PeriodEnd.ToString("yyyy-MM-dd"), p.AmountDue, p.AmountPaid, p.Method, p.Note,
        }));

    /// <summary>
    /// The profit report as one sheet: the summary block, then the per-day series, so the
    /// figures the owner sees on screen can be checked line by line.
    /// </summary>
    public static string ProfitReport(DateRange range)
    {
        var f = Finance.For(range);
        // Collection-expression elements would be parsed as indexer initialisers inside a
        // collection initialiser, so each row is an explicit array.
        object?[] Row(string label, object? value, string meaning = "") => new object?[] { label, value, meaning };

        var rows = new List<object?[]>
        {
            Row("Period", range.Label),
            Row("From", range.From.ToString("yyyy-MM-dd")),
            Row("To", range.To.AddDays(-1).ToString("yyyy-MM-dd")),
            Row(string.Empty, string.Empty),
            Row("Revenue", f.Revenue, "Completed sales, less refunds"),
            Row("Cost of goods sold", f.Cogs, "Cost of the items actually sold"),
            Row("Gross profit", f.GrossProfit, "Revenue - COGS"),
            Row("Operating expenses", f.OperatingExpenses, "Rent, power, water and the rest"),
            Row("Worker salaries", f.SalaryExpense, "Salary payments made in the period"),
            Row("Stock written off", f.StockLosses, "Damaged, expired, lost or stolen, at cost"),
            Row("Net profit", f.NetProfit, "Gross profit - operating costs"),
            Row(string.Empty, string.Empty),
            Row("Cash collected", f.CashCollected),
            Row("Card collected", f.CardCollected),
            Row("Supplier payments", f.SupplierPayments, "Money paid out to suppliers"),
            Row("Stock received", f.StockPurchased, "Value delivered - an asset, not an expense"),
            Row("Money spent", f.MoneySpent, "Supplier payments + expenses + salaries"),
            Row(string.Empty, string.Empty),
            Row("Sales", f.SaleCount),
            Row("Items sold", f.ItemsSold),
            Row("Average basket", f.AverageBasket),
            Row("Discounts given", f.Discounts),
            Row(string.Empty, string.Empty),
            Row("Date", "Revenue", "Profit"),
        };

        var revenue = Finance.Series(range, SeriesKind.Revenue);
        var profit = Finance.Series(range, SeriesKind.Profit);
        for (var i = 0; i < revenue.Count; i++)
            rows.Add(new object?[]
            {
                revenue[i].At.ToString("yyyy-MM-dd"), revenue[i].Value,
                i < profit.Count ? profit[i].Value : 0m,
            });

        return Write("profit-report", new[] { "Figure", "Value", "Meaning" }, rows);
    }

    // ------------------------------- Plumbing -------------------------------

    private static string Write(string name, IReadOnlyList<string> headers,
                                IEnumerable<object?[]> rows)
    {
        Session.Require(Permission.ExportData);

        var path = Path.Combine(Folder, $"{name}-{DateTime.Now:yyyy-MM-dd-HHmm}.csv");
        var text = new StringBuilder();

        text.AppendLine(string.Join(",", headers.Select(Escape)));
        var count = 0;
        foreach (var row in rows)
        {
            text.AppendLine(string.Join(",", row.Select(Cell)));
            count++;
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));

        ActivityRepository.Record("exported data", "Export", name, newValue: $"{count} rows",
            detail: $"exported {count} rows of {name.Replace('-', ' ')}");
        return path;
    }

    /// <summary>Decimals are written invariant so a comma decimal separator cannot split a column.</summary>
    private static string Cell(object? value) => value switch
    {
        null => string.Empty,
        decimal d => d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        double d => d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        _ => Escape(value.ToString() ?? string.Empty),
    };

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;

    /// <summary>Tells the owner where the file went, and offers to open the folder.</summary>
    public static void Announce(Window? owner, string path)
    {
        if (owner is null) return;

        var open = Views.ConfirmWindow.Ask(owner, "Export saved",
            $"{Path.GetFileName(path)} was saved to {Path.GetDirectoryName(path)}.\n\nOpen the folder?");

        if (!open) return;

        // /select, puts the new file in view rather than dropping the owner in a folder
        // of forty exports and letting them hunt.
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // If Explorer will not start, the path was already spelled out above.
        }
    }
}
