using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// The shop's business, asked for over the wire.
///
/// <para>
/// Each of these is one HTTP call to the machine that owns the database, where the same
/// repository runs that the shop's own back office calls. Nothing is decided here: a refusal
/// arrives as the shop's own words and is thrown, not returned, so a screen written against a
/// repository that throws keeps behaving as it always did.
/// </para>
///
/// <para>
/// Reads return what the shop said. They never return an empty list because the shop could not
/// be reached — that throws, because a blank supplier list and a dead network look identical on
/// a screen and only one of them means the shop has no suppliers.
/// </para>
/// </summary>
public sealed class RemoteSuppliers : ISupplierService
{
    public List<Supplier> List(bool includeInactive = false, string? search = null)
    {
        var query = $"suppliers?includeInactive={includeInactive}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";

        return Api.Get<List<Supplier>>(query) ?? new List<Supplier>();
    }

    public int Create(Supplier supplier) => Api.Must(Api.Post<Saved>("suppliers", supplier)).Id;

    public void Update(Supplier supplier) =>
        Api.Must(Api.Put<Saved>($"suppliers/{supplier.Id}", supplier));

    public void SetActive(int id, string name, bool active) =>
        Api.Must(Api.Put<Saved>($"suppliers/{id}/active", new SetActive(active)));

    public List<SupplierGoods> WhatWeBuy(int supplierId) =>
        Api.Get<List<SupplierGoods>>($"suppliers/{supplierId}/goods") ?? new List<SupplierGoods>();

    public List<Purchase> Purchases(DateRange? range = null, int? supplierId = null) =>
        Api.Get<List<Purchase>>("suppliers/deliveries" + Api.When(range, supplierId is { } s ? $"supplierId={s}" : null))
        ?? new List<Purchase>();

    public List<PurchaseLine> PurchaseLines(int purchaseId) =>
        Api.Get<List<PurchaseLine>>($"suppliers/deliveries/{purchaseId}/lines") ?? new List<PurchaseLine>();

    public int RecordPurchase(Purchase purchase, decimal amountPaidNow) =>
        Api.Must(Api.Post<Saved>($"suppliers/{purchase.SupplierId}/deliveries",
                                 new RecordDelivery(purchase, amountPaidNow))).Id;

    public void CancelPurchase(int purchaseId, string reason) =>
        Api.Must(Api.Post<Saved>($"suppliers/deliveries/{purchaseId}/cancel", new Reason(reason)));

    public List<SupplierPayment> Payments(DateRange? range = null, int? supplierId = null) =>
        Api.Get<List<SupplierPayment>>("suppliers/payments" + Api.When(range, supplierId is { } s ? $"supplierId={s}" : null))
        ?? new List<SupplierPayment>();

    public void Pay(int supplierId, string supplierName, decimal amount, DateTime paidOn,
                    string method = "Cash", string note = "", int? purchaseId = null) =>
        Api.Must(Api.Post<Saved>($"suppliers/{supplierId}/payments",
                                 new PaySupplier(supplierName, amount, paidOn, method, note, purchaseId)));
}

public sealed class RemoteExpenses : IExpenseService
{
    public List<Expense> List(DateRange? range = null, int? categoryId = null, string? search = null)
    {
        var extra = new List<string>();
        if (categoryId is { } id) extra.Add($"categoryId={id}");
        if (!string.IsNullOrWhiteSpace(search)) extra.Add($"search={Uri.EscapeDataString(search)}");

        return Api.Get<List<Expense>>("expenses" + Api.When(range, extra.ToArray())) ?? new List<Expense>();
    }

    public int Create(Expense expense) => Api.Must(Api.Post<Saved>("expenses", expense)).Id;

    public void Update(Expense expense) => Api.Must(Api.Put<Saved>($"expenses/{expense.Id}", expense));

    public void Void(int id, string name, string reason) =>
        Api.Must(Api.Post<Saved>($"expenses/{id}/void", new Reason(reason)));

    public List<(string Category, decimal Amount)> ByCategory(DateRange range) =>
        (Api.Get<List<NamedAmount>>("expenses/by-category" + Api.When(range)) ?? new List<NamedAmount>())
        .Select(a => (a.Name, a.Amount)).ToList();

    public decimal Total(DateRange range) =>
        Api.Get<Amount>("expenses/total" + Api.When(range))?.Value ?? 0m;

    public List<(int Id, string Name)> Categories() =>
        (Api.Get<List<NamedId>>("expenses/categories") ?? new List<NamedId>())
        .Select(c => (c.Id, c.Name)).ToList();

    public int AddCategory(string name) =>
        Api.Must(Api.Post<Saved>("expenses/categories", new Named(name))).Id;
}

public sealed class RemoteWorkers : IWorkerService
{
    public List<Worker> List(bool includeInactive = false) =>
        Api.Get<List<Worker>>($"employees?includeInactive={includeInactive}") ?? new List<Worker>();

    public int Create(Worker worker) => Api.Must(Api.Post<Saved>("employees", worker)).Id;

    public void Update(Worker worker) => Api.Must(Api.Put<Saved>($"employees/{worker.Id}", worker));

    public void SetActive(int id, string name, bool active) =>
        Api.Must(Api.Put<Saved>($"employees/{id}/active", new SetActive(active)));

    public void SetPin(int id, string pin) =>
        Api.Must(Api.Put<Saved>($"employees/{id}/password", new Secret(pin)));

    public List<SalaryLedger> Ledger(DateRange period) =>
        Api.Get<List<SalaryLedger>>("employees/salaries" + Api.When(period)) ?? new List<SalaryLedger>();

    public void PaySalary(int workerId, string workerName, decimal amountDue, decimal amountPaid,
                          DateRange period, DateTime paidOn, string method, string note) =>
        Api.Must(Api.Post<Saved>($"employees/{workerId}/salary-payments",
                                 new PaySalaryNow(workerName, amountDue, amountPaid,
                                                  period.From, period.To, paidOn, method, note)));

    public decimal PaidIn(DateRange range) =>
        Api.Get<Amount>("employees/paid" + Api.When(range))?.Value ?? 0m;
}

public sealed class RemoteSales : ISalesService
{
    public List<SaleSummaryEx> List(DateRange? range = null, string? search = null,
                                             int? workerId = null, PaymentMethod? method = null,
                                             int? productId = null, int? categoryId = null)
    {
        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) extra.Add($"search={Uri.EscapeDataString(search)}");
        if (workerId is { } w) extra.Add($"workerId={w}");
        if (method is { } m) extra.Add($"method={m}");
        if (productId is { } p) extra.Add($"productId={p}");
        if (categoryId is { } c) extra.Add($"categoryId={c}");

        return Api.Get<List<SaleSummaryEx>>("sales" + Api.When(range, extra.ToArray()))
               ?? new List<SaleSummaryEx>();
    }

    public SaleDetail? Find(int invoiceNumber) => Api.Get<SaleDetail>($"sales/{invoiceNumber}");

    public void Refund(int invoiceNumber, IReadOnlyList<(int SaleLineId, decimal Quantity)> items,
                       string reason, bool restock) =>
        Api.Must(Api.Post<Saved>($"sales/{invoiceNumber}/refunds",
                                 new RefundSale(items.Select(i => new RefundLine(i.SaleLineId, i.Quantity)).ToList(),
                                                reason, restock)));

    public void Cancel(int invoiceNumber, string reason) =>
        Api.Must(Api.Post<Saved>($"sales/{invoiceNumber}/cancel", new Reason(reason)));

    public List<SalesHistoryRepository.ProductStat> ProductPerformance(DateRange range) =>
        Api.Get<List<SalesHistoryRepository.ProductStat>>("sales/products" + Api.When(range))
        ?? new List<SalesHistoryRepository.ProductStat>();

    public List<int> WhoSoldIn(DateRange range) =>
        Api.Get<List<int>>("sales/cashiers" + Api.When(range)) ?? new List<int>();
}

public sealed class RemoteActivity : IActivityService
{
    public List<ActivityEntry> List(DateRange? range = null, string? search = null, int limit = 300)
    {
        var extra = new List<string> { $"limit={limit}" };
        if (!string.IsNullOrWhiteSpace(search)) extra.Add($"search={Uri.EscapeDataString(search)}");

        return Api.Get<List<ActivityEntry>>("activity" + Api.When(range, extra.ToArray()))
               ?? new List<ActivityEntry>();
    }
}

public sealed class RemoteReports : IReportService
{
    public Financials Money(DateRange range) =>
        Api.Get<Financials>("reports/money" + Api.When(range))
        ?? throw new ShopUnreachable("The shop did not send its figures.");

    public List<Finance.Point> Series(DateRange range, SeriesKind kind) =>
        Api.Get<List<Finance.Point>>($"reports/series" + Api.When(range, $"kind={kind}"))
        ?? new List<Finance.Point>();

    public List<Alert> Alerts() => Api.Get<List<Alert>>("reports/alerts") ?? new List<Alert>();

    public List<(StockReason Reason, decimal Quantity, decimal Value)> LossesByReason(DateRange range) =>
        (Api.Get<List<LossLine>>("reports/losses" + Api.When(range)) ?? new List<LossLine>())
        .Select(l => (l.Reason, l.Quantity, l.Value)).ToList();
}

// ---------------------------------------------------------------- what crosses the wire

public sealed record Saved(bool Ok, int Id, string Problem, string Refusal);

public sealed record SetActive(bool Active);

public sealed record Reason(string Text);

public sealed record Named(string Name);

public sealed record NamedId(int Id, string Name);

public sealed record NamedAmount(string Name, decimal Amount);

public sealed record Amount(decimal Value);

public sealed record Secret(string Value);

public sealed record RecordDelivery(Purchase Purchase, decimal AmountPaidNow);

public sealed record PaySupplier(string SupplierName, decimal AmountValue, DateTime PaidOn,
                                 string Method, string Note, int? PurchaseId);

public sealed record PaySalaryNow(string WorkerName, decimal AmountDue, decimal AmountPaid,
                                  DateTime PeriodFrom, DateTime PeriodTo, DateTime PaidOn,
                                  string Method, string Note);

public sealed record RefundLine(int SaleLineId, decimal Quantity);

public sealed record RefundSale(IReadOnlyList<RefundLine> Lines, string Reason, bool Restock);
