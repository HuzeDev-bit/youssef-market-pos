using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// The shop's business, on the machine that owns it.
///
/// Every one of these is the repository it always was. Nothing is re-implemented, nothing is
/// re-decided: the permission checks, the transactions, the refusals and the audit entries stay
/// exactly where they were written, which is why the shop's own back office behaves today
/// precisely as it did before any of this existed.
/// </summary>
public sealed class LocalSuppliers : ISupplierService
{
    public List<Supplier> List(bool includeInactive = false, string? search = null) =>
        SupplierRepository.List(includeInactive, search);

    public int Create(Supplier supplier) => SupplierRepository.Create(supplier);

    public void Update(Supplier supplier) => SupplierRepository.Update(supplier);

    public void SetActive(int id, string name, bool active) =>
        SupplierRepository.SetActive(id, name, active);

    public bool Delete(int id, string name, out bool removed, out string problem) =>
        SupplierRepository.Delete(id, name, out removed, out problem);

    public List<SupplierGoods> WhatWeBuy(int supplierId) =>
        SupplierRepository.WhatWeBuy(supplierId);

    public List<Purchase> Purchases(DateRange? range = null, int? supplierId = null) =>
        SupplierRepository.ListPurchases(range, supplierId);

    public List<PurchaseLine> PurchaseLines(int purchaseId) =>
        SupplierRepository.ListPurchaseLines(purchaseId);

    public int RecordPurchase(Purchase purchase, decimal amountPaidNow) =>
        SupplierRepository.RecordPurchase(purchase, amountPaidNow);

    public void CancelPurchase(int purchaseId, string reason) =>
        SupplierRepository.CancelPurchase(purchaseId, reason);

    public List<SupplierPayment> Payments(DateRange? range = null, int? supplierId = null) =>
        SupplierRepository.ListPayments(range, supplierId);

    public void Pay(int supplierId, string supplierName, decimal amount, DateTime paidOn,
                    string method = "Cash", string note = "", int? purchaseId = null) =>
        SupplierRepository.Pay(supplierId, supplierName, amount, paidOn, method, note, purchaseId);
}

public sealed class LocalExpenses : IExpenseService
{
    public List<Expense> List(DateRange? range = null, int? categoryId = null, string? search = null) =>
        ExpenseRepository.List(range, categoryId, search);

    public int Create(Expense expense) => ExpenseRepository.Create(expense);

    public void Update(Expense expense) => ExpenseRepository.Update(expense);

    public void Void(int id, string name, string reason) => ExpenseRepository.Void(id, name, reason);

    public List<(string Category, decimal Amount)> ByCategory(DateRange range) =>
        ExpenseRepository.ByCategory(range);

    public decimal Total(DateRange range) => ExpenseRepository.Total(range);

    public List<(int Id, string Name)> Categories() => ExpenseRepository.Categories();

    public int AddCategory(string name) => ExpenseRepository.AddCategory(name);
}

public sealed class LocalWorkers : IWorkerService
{
    public List<Worker> List(bool includeInactive = false) =>
        WorkerRepository.List(includeInactive);

    public int Create(Worker worker) => WorkerRepository.Create(worker);

    public void Update(Worker worker) => WorkerRepository.Update(worker);

    public void SetActive(int id, string name, bool active) =>
        WorkerRepository.SetActive(id, name, active);

    public void SetPin(int id, string pin) => WorkerRepository.SetPin(id, pin);

    public List<SalaryLedger> Ledger(DateRange period) => WorkerRepository.Ledger(period);

    public void PaySalary(int workerId, string workerName, decimal amountDue, decimal amountPaid,
                          DateRange period, DateTime paidOn, string method, string note) =>
        WorkerRepository.PaySalary(workerId, workerName, amountDue, amountPaid, period, paidOn, method, note);

    public decimal PaidIn(DateRange range) => WorkerRepository.PaidIn(range);
}

public sealed class LocalSales : ISalesService
{
    public List<SaleSummaryEx> List(DateRange? range = null, string? search = null,
                                             int? workerId = null, PaymentMethod? method = null,
                                             int? productId = null, int? categoryId = null) =>
        SalesHistoryRepository.List(range, search, workerId, method, productId, categoryId);

    public SaleDetail? Find(int invoiceNumber) => SalesHistoryRepository.Find(invoiceNumber);

    public void Refund(int invoiceNumber, IReadOnlyList<(int SaleLineId, decimal Quantity)> items,
                       string reason, bool restock) =>
        SalesHistoryRepository.Refund(invoiceNumber, items, reason, restock);

    public void Cancel(int invoiceNumber, string reason) =>
        SalesHistoryRepository.Cancel(invoiceNumber, reason);

    public List<SalesHistoryRepository.ProductStat> ProductPerformance(DateRange range) =>
        SalesHistoryRepository.ProductPerformance(range);

    public List<int> WhoSoldIn(DateRange range) =>
        SalesHistoryRepository.WhoSoldIn(range).ToList();
}

public sealed class LocalActivity : IActivityService
{
    public List<ActivityEntry> List(DateRange? range = null, string? search = null, int limit = 300) =>
        ActivityRepository.List(range, search, limit);
}

public sealed class LocalReports : IReportService
{
    public Financials Money(DateRange range) => Finance.For(range);

    public List<Finance.Point> Series(DateRange range, SeriesKind kind) =>
        Finance.Series(range, kind);

    public List<Alert> Alerts() => Notifications.Build();

    public List<(StockReason Reason, decimal Quantity, decimal Value)> LossesByReason(DateRange range) =>
        InventoryRepository.LossesByReason(range);
}
