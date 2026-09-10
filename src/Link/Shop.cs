using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// Where a screen asks for the shop's business.
///
/// <para>
/// Every page in this app used to call a repository, which opened the database on the machine
/// the page was running on. That is right on the machine that owns the shop and wrong on every
/// other one: a cashier's computer has no database, and a back office opened there would be a
/// second, empty shop.
/// </para>
///
/// <para>
/// So a page asks <c>Shop.Products</c>, <c>Shop.Categories</c> and the rest, and gets whichever
/// implementation belongs to the machine it is on. On the shop's own machine that is the
/// repository, unchanged, with its transactions and its rules. On any other it is an HTTP
/// client talking to the shop, where the same repository runs — so the rules, the validation
/// and the audit trail are enforced in one place and cannot be argued with by a client.
/// </para>
///
/// <para>
/// The interfaces are written in the shop's own terms — products, deliveries, expenses — and
/// not as a way of naming repository methods over a wire. What crosses the network is a request
/// to do a thing to a business, and the server decides whether that may happen.
/// </para>
/// </summary>
public static class Shop
{
    private static bool Remote => Services.Catalog.BelongsToAServer;

    private static ICategoryService? _categories;
    private static IStockService? _stock;
    private static ISupplierService? _suppliers;
    private static IExpenseService? _expenses;
    private static IWorkerService? _workers;
    private static ISalesService? _sales;
    private static IActivityService? _activity;
    private static IReportService? _reports;

    /// <summary>How the shop files what it sells.</summary>
    public static ICategoryService Categories =>
        _categories ??= Remote ? new RemoteCategories() : new LocalCategories();

    /// <summary>What the shop sells, and what is on its shelves.</summary>
    public static IStockService Stock =>
        _stock ??= Remote ? new RemoteStock() : new LocalStock();

    /// <summary>Who the shop buys from, what arrived, and what has been paid.</summary>
    public static ISupplierService Suppliers =>
        _suppliers ??= Remote ? new RemoteSuppliers() : new LocalSuppliers();

    /// <summary>What the shop spends on running itself.</summary>
    public static IExpenseService Expenses =>
        _expenses ??= Remote ? new RemoteExpenses() : new LocalExpenses();

    /// <summary>Who works here, and what they are owed.</summary>
    public static IWorkerService Workers =>
        _workers ??= Remote ? new RemoteWorkers() : new LocalWorkers();

    /// <summary>What has been sold, refunded and cancelled.</summary>
    public static ISalesService Sales =>
        _sales ??= Remote ? new RemoteSales() : new LocalSales();

    /// <summary>What has been done, and by whom.</summary>
    public static IActivityService Activity =>
        _activity ??= Remote ? new RemoteActivity() : new LocalActivity();

    /// <summary>The figures: takings, cost, profit, what needs attention.</summary>
    public static IReportService Reports =>
        _reports ??= Remote ? new RemoteReports() : new LocalReports();

    /// <summary>
    /// Forgets which implementations were chosen, for the diagnostics that switch a process
    /// between being a shop and being a till.
    /// </summary>
    public static void Reconsider()
    {
        _categories = null;
        _stock = null;
        _suppliers = null;
        _expenses = null;
        _workers = null;
        _sales = null;
        _activity = null;
        _reports = null;
    }
}

/// <summary>What the app needs to do with the shop's categories.</summary>
public interface ICategoryService
{
    List<Models.CategoryRow> List(bool includeInactive = false);

    int Create(string name, string icon = "", string image = "");

    void Rename(int id, string oldName, string newName, string icon, string image);

    /// <summary>Deletes a category outright. False with a reason when the shop refuses.</summary>
    bool Delete(int id, string name, out string problem);

    /// <summary>Hides or restores one. False with a reason when the shop refuses.</summary>
    bool SetActive(int id, string name, bool active, out string problem);
}
