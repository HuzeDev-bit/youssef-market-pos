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

    /// <summary>How the shop files what it sells.</summary>
    public static ICategoryService Categories =>
        _categories ??= Remote ? new RemoteCategories() : new LocalCategories();

    /// <summary>
    /// Forgets which implementations were chosen, for the diagnostics that switch a process
    /// between being a shop and being a till.
    /// </summary>
    public static void Reconsider() => _categories = null;
}

/// <summary>What the app needs to do with the shop's categories.</summary>
public interface ICategoryService
{
    IReadOnlyList<Models.CategoryRow> List(bool includeInactive = false);

    int Create(string name, string icon = "", string image = "");

    void Rename(int id, string oldName, string newName, string icon, string image);

    /// <summary>Deletes a category outright. False with a reason when the shop refuses.</summary>
    bool Delete(int id, string name, out string problem);

    /// <summary>Hides or restores one. False with a reason when the shop refuses.</summary>
    bool SetActive(int id, string name, bool active, out string problem);
}
