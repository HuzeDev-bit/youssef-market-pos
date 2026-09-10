using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// What the shop does when the back office asks it something, whichever machine the back office
/// is running on.
///
/// <para>
/// Every method here runs on the machine that owns the database, under the identity the caller's
/// token names — see <see cref="Authorised"/> — and every one of them calls the repository the
/// shop's own back office calls. So the permission checks, the transactions, the stock rules and
/// the audit entries are written once and enforced once, and a remote screen cannot do anything
/// the shop would not let the person in front of it do.
/// </para>
///
/// <para>
/// Writes answer with <see cref="Saved"/> rather than throwing across the wire, and carry the
/// kind of refusal it was, so the client can raise the same exception the page was written to
/// catch. A refusal is an answer; a shop that cannot be reached is not.
/// </para>
/// </summary>
public static class ShopBusinessApi
{
    /// <summary>
    /// A date range when the caller gave one, and nothing when they did not.
    ///
    /// Every list in the back office narrows by date, and every one of them treats "no range"
    /// as "everything". Written here so both servers read a query string the same way.
    /// </summary>
    public static DateRange? Span(DateTime? from, DateTime? to) =>
        from is { } f && to is { } t ? DateRange.Custom(f, t) : null;

    /// <summary>Runs a write and describes what happened, refusals included.</summary>
    private static Saved Try(Func<int> work)
    {
        try
        {
            return new Saved(true, work(), string.Empty, string.Empty);
        }
        catch (NotEnoughStockException shortfall)
        {
            return new Saved(false, 0, shortfall.Message, nameof(NotEnoughStockException));
        }
        catch (UnauthorizedAccessException refused)
        {
            return new Saved(false, 0, refused.Message, nameof(UnauthorizedAccessException));
        }
        catch (ArgumentException wrong)
        {
            return new Saved(false, 0, wrong.Message, nameof(ArgumentException));
        }
        catch (Exception problem)
        {
            return new Saved(false, 0, problem.Message, nameof(Exception));
        }
    }

    private static Saved Try(Action work) => Try(() => { work(); return 0; });

    // ================================================================ suppliers

    public static List<Supplier> Suppliers(bool includeInactive, string? search) =>
        SupplierRepository.List(includeInactive, search);

    public static Saved CreateSupplier(Supplier supplier) => Try(() => SupplierRepository.Create(supplier));

    public static Saved UpdateSupplier(Supplier supplier) => Try(() => SupplierRepository.Update(supplier));

    public static Saved SetSupplierActive(int id, bool active) => Try(() =>
        SupplierRepository.SetActive(id, NameOfSupplier(id), active));

    /// <summary>
    /// Removing a supplier. The shop decides whether the row can go or is only hidden, and
    /// says which, because a client told "done" would have no way of knowing the row is still
    /// on the list.
    /// </summary>
    public static SupplierGone DeleteSupplier(int id)
    {
        var name = NameOfSupplier(id);

        try
        {
            var ok = SupplierRepository.Delete(id, name, out var removed, out var problem);
            return new SupplierGone(ok, id, problem, string.Empty, removed);
        }
        catch (UnauthorizedAccessException refused)
        {
            return new SupplierGone(false, id, refused.Message,
                                    nameof(UnauthorizedAccessException), false);
        }
        catch (Exception problem)
        {
            return new SupplierGone(false, id, problem.Message, string.Empty, false);
        }
    }

    public static List<SupplierGoods> SupplierGoods(int id) => SupplierRepository.WhatWeBuy(id);

    public static List<Purchase> Deliveries(DateRange? range, int? supplierId) =>
        SupplierRepository.ListPurchases(range, supplierId);

    public static List<PurchaseLine> DeliveryLines(int purchaseId) =>
        SupplierRepository.ListPurchaseLines(purchaseId);

    public static Saved RecordDelivery(RecordDelivery asked) =>
        Try(() => SupplierRepository.RecordPurchase(asked.Purchase, asked.AmountPaidNow));

    public static Saved CancelDelivery(int purchaseId, string reason) =>
        Try(() => SupplierRepository.CancelPurchase(purchaseId, reason));

    public static List<SupplierPayment> SupplierPayments(DateRange? range, int? supplierId) =>
        SupplierRepository.ListPayments(range, supplierId);

    public static Saved PaySupplier(int supplierId, PaySupplier asked) => Try(() =>
        SupplierRepository.Pay(supplierId, asked.SupplierName, asked.AmountValue, asked.PaidOn,
                               asked.Method, asked.Note, asked.PurchaseId));

    private static string NameOfSupplier(int id) =>
        SupplierRepository.List(includeInactive: true).FirstOrDefault(s => s.Id == id)?.Name ?? string.Empty;

    // ================================================================ expenses

    public static List<Expense> Expenses(DateRange? range, int? categoryId, string? search) =>
        ExpenseRepository.List(range, categoryId, search);

    public static Saved CreateExpense(Expense expense) => Try(() => ExpenseRepository.Create(expense));

    public static Saved UpdateExpense(Expense expense) => Try(() => ExpenseRepository.Update(expense));

    public static Saved VoidExpense(int id, string reason) => Try(() =>
        ExpenseRepository.Void(id, NameOfExpense(id), reason));

    public static List<NamedAmount> ExpensesByCategory(DateRange range) =>
        ExpenseRepository.ByCategory(range).Select(e => new NamedAmount(e.Category, e.Amount)).ToList();

    public static Amount ExpenseTotal(DateRange range) => new(ExpenseRepository.Total(range));

    public static List<NamedId> ExpenseCategories() =>
        ExpenseRepository.Categories().Select(c => new NamedId(c.Id, c.Name)).ToList();

    public static Saved AddExpenseCategory(string name) => Try(() => ExpenseRepository.AddCategory(name));

    private static string NameOfExpense(int id) =>
        ExpenseRepository.List().FirstOrDefault(e => e.Id == id)?.Name ?? string.Empty;

    // ================================================================ employees

    public static List<Worker> Employees(bool includeInactive) =>
        WorkerRepository.List(includeInactive);

    public static Saved CreateEmployee(Worker worker) => Try(() => WorkerRepository.Create(worker));

    public static Saved UpdateEmployee(Worker worker) => Try(() => WorkerRepository.Update(worker));

    public static Saved SetEmployeeActive(int id, bool active) => Try(() =>
        WorkerRepository.SetActive(id, WorkerRepository.Find(id)?.Name ?? string.Empty, active));

    public static Saved SetEmployeePassword(int id, string password) =>
        Try(() => WorkerRepository.SetPin(id, password));

    public static List<SalaryLedger> Salaries(DateRange period) => WorkerRepository.Ledger(period);

    public static Saved PaySalary(int workerId, PaySalaryNow asked) => Try(() =>
        WorkerRepository.PaySalary(workerId, asked.WorkerName, asked.AmountDue, asked.AmountPaid,
                                   DateRange.Custom(asked.PeriodFrom, asked.PeriodTo),
                                   asked.PaidOn, asked.Method, asked.Note));

    public static Amount SalariesPaidIn(DateRange range) => new(WorkerRepository.PaidIn(range));

    // ================================================================ sales

    public static List<SaleSummaryEx> Sales(DateRange? range, string? search, int? workerId,
                                                     PaymentMethod? method, int? productId, int? categoryId) =>
        SalesHistoryRepository.List(range, search, workerId, method, productId, categoryId);

    public static SaleDetail? Sale(int invoiceNumber) => SalesHistoryRepository.Find(invoiceNumber);

    public static Saved RefundSale(int invoiceNumber, RefundSale asked) => Try(() =>
        SalesHistoryRepository.Refund(
            invoiceNumber,
            asked.Lines.Select(l => (l.SaleLineId, l.Quantity)).ToList(),
            asked.Reason,
            asked.Restock));

    public static Saved CancelSale(int invoiceNumber, string reason) =>
        Try(() => SalesHistoryRepository.Cancel(invoiceNumber, reason));

    public static List<SalesHistoryRepository.ProductStat> ProductPerformance(DateRange range) =>
        SalesHistoryRepository.ProductPerformance(range);

    public static List<int> WhoSoldIn(DateRange range) =>
        SalesHistoryRepository.WhoSoldIn(range).ToList();

    // ================================================================ activity

    public static List<ActivityEntry> Activity(DateRange? range, string? search, int limit) =>
        ActivityRepository.List(range, search, limit);

    // ================================================================ the figures

    public static Financials Money(DateRange range) => Finance.For(range);

    public static List<Finance.Point> Series(DateRange range, SeriesKind kind) =>
        Finance.Series(range, kind);

    public static List<Alert> Alerts() => Notifications.Build();

    public static List<LossLine> Losses(DateRange range) =>
        InventoryRepository.LossesByReason(range)
            .Select(l => new LossLine(l.Reason, l.Quantity, l.Value)).ToList();

    // ================================================================ products and shelves

    public static List<StockItem> Products(string? search, int? categoryId, bool includeInactive) =>
        StockRepository.List(search: search, categoryId: categoryId, includeInactive: includeInactive);

    public static StockItem? Product(int id) => StockRepository.Find(id);

    public static StockItem? ProductByBarcode(string barcode) => StockRepository.FindByBarcode(barcode);

    public static List<StockItem> RecentProducts() => StockRepository.RecentlyAdded();

    public static List<StockItem> LowStock() => StockRepository.LowStock();

    public static List<StockItem> OutOfStock() => StockRepository.OutOfStock();

    public static List<StockItem> Expiring(int withinDays) => StockRepository.Expiring(withinDays);

    public static Answered BarcodeTaken(string barcode, int exceptId) =>
        new(StockRepository.BarcodeTaken(barcode, exceptId));

    public static Saved CreateProduct(SaveProduct asked) =>
        Try(() => StockRepository.Create(asked.Item, asked.OpeningStock));

    public static Saved UpdateProduct(SaveProduct asked) => Try(() => StockRepository.Update(asked.Item));

    public static Saved SetProductActive(int id, bool active) => Try(() =>
        StockRepository.SetActive(id, StockRepository.Find(id)?.Name ?? string.Empty, active));

    public static Saved ReceiveStock(int id, ReceiveStock asked) => Try(() =>
        StockRepository.ReceiveAtTill(id, asked.Quantity, asked.Cost, asked.Price, asked.ExpiresOn));

    public static List<StockMovement> Movements(DateRange? range, int? productId) =>
        InventoryRepository.ListMovements(range, productId);

    public static Saved CountShelf(int id, CountShelf asked) => Try(() =>
        InventoryRepository.SetCount(id, StockRepository.Find(id)?.Name ?? string.Empty,
                                     asked.Counted, asked.Note));

    public static Saved AdjustStock(int id, AdjustStock asked) => Try(() =>
        InventoryRepository.Adjust(id, StockRepository.Find(id)?.Name ?? string.Empty, asked.Delta,
                                 Enum.TryParse<StockReason>(asked.Reason, out var why) ? why : StockReason.ManualCorrection,
                                 reference: asked.Reference, note: asked.Note, unitCost: asked.UnitCost));
}
