using MarketPos.Models;

namespace MarketPos.Link;

/// <summary>
/// What the back office needs of the shop, written in the shop's own terms.
///
/// <para>
/// One interface per part of the business, and each method is a thing a shopkeeper does rather
/// than a name borrowed from a repository. Two implementations answer them: on the machine that
/// owns the database, the repositories, unchanged; on any other, an HTTP call to the machine
/// that does. The pages call neither directly — they call <see cref="Shop"/>, and never learn
/// which of the two they got.
/// </para>
/// </summary>
public interface ISupplierService
{
    List<Supplier> List(bool includeInactive = false, string? search = null);

    int Create(Supplier supplier);

    void Update(Supplier supplier);

    void SetActive(int id, string name, bool active);

    List<SupplierGoods> WhatWeBuy(int supplierId);

    // ---- deliveries ----

    List<Purchase> Purchases(DateRange? range = null, int? supplierId = null);

    List<PurchaseLine> PurchaseLines(int purchaseId);

    int RecordPurchase(Purchase purchase, decimal amountPaidNow);

    void CancelPurchase(int purchaseId, string reason);

    // ---- money ----

    List<SupplierPayment> Payments(DateRange? range = null, int? supplierId = null);

    void Pay(int supplierId, string supplierName, decimal amount, DateTime paidOn,
             string method = "Cash", string note = "", int? purchaseId = null);
}

public interface IExpenseService
{
    List<Expense> List(DateRange? range = null, int? categoryId = null, string? search = null);

    int Create(Expense expense);

    void Update(Expense expense);

    void Void(int id, string name, string reason);

    List<(string Category, decimal Amount)> ByCategory(DateRange range);

    decimal Total(DateRange range);

    List<(int Id, string Name)> Categories();

    int AddCategory(string name);
}

public interface IWorkerService
{
    List<Worker> List(bool includeInactive = false);

    int Create(Worker worker);

    void Update(Worker worker);

    void SetActive(int id, string name, bool active);

    void SetPin(int id, string pin);

    List<SalaryLedger> Ledger(DateRange period);

    void PaySalary(int workerId, string workerName, decimal amountDue, decimal amountPaid,
                   DateRange period, DateTime paidOn, string method, string note);

    decimal PaidIn(DateRange range);
}

public interface ISalesService
{
    List<Data.SaleSummaryEx> List(DateRange? range = null, string? search = null,
                                           int? workerId = null, PaymentMethod? method = null,
                                           int? productId = null, int? categoryId = null);

    Data.SaleDetail? Find(int invoiceNumber);

    void Refund(int invoiceNumber, IReadOnlyList<(int SaleLineId, decimal Quantity)> items,
                string reason, bool restock);

    void Cancel(int invoiceNumber, string reason);

    List<Data.SalesHistoryRepository.ProductStat> ProductPerformance(DateRange range);

    List<int> WhoSoldIn(DateRange range);
}

public interface IActivityService
{
    List<ActivityEntry> List(DateRange? range = null, string? search = null, int limit = 300);
}

/// <summary>
/// The figures every screen shows, worked out in one place.
///
/// Deliberately not "give me the rows and I will add them up": the arithmetic that turns sales
/// and costs into profit belongs on the machine that owns the records, so the dashboard, the
/// reports and the export cannot drift into three answers to the same question.
/// </summary>
public interface IReportService
{
    Services.Financials Money(DateRange range);

    List<Services.Finance.Point> Series(DateRange range, Services.SeriesKind kind);

    List<Alert> Alerts();

    List<(StockReason Reason, decimal Quantity, decimal Value)> LossesByReason(DateRange range);
}
