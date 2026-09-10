using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>
/// The product catalogue, backed by SQLite.
///
/// A new shop starts empty. It used to open with two dozen invented products — Baguette,
/// Tomatoes, Coca-Cola — so a fresh install had something to click. That is a demo, not a
/// shop: the owner then has to find and delete every one of them before the till tells the
/// truth, and any they miss are on the shelf as far as the accounts are concerned.
/// </summary>
public static class Catalog
{
    private static List<Product>? _products;
    private static List<string>? _categories;

    /// <summary>Creates the schema, then loads the shop's own products into memory.</summary>
    public static void Load()
    {
        Database.Initialize();
        Reload();
    }

    /// <summary>
    /// Whether this copy is a till, whose catalogue belongs to a server and not to this
    /// computer.
    ///
    /// Set once at start-up. While it is on, nothing here ever reads the products table — not
    /// on the first load, not on a reload, not after a sale. A till that could fall back to
    /// its own database would eventually do it, and what it would show is a shop that exists
    /// on one laptop and in no set of books anywhere.
    /// </summary>
    public static bool BelongsToAServer { get; set; }

    /// <summary>
    /// The catalogue the server last sent, held in memory and written down nowhere.
    ///
    /// This is the whole of what a till knows about what the shop sells. It arrives over the
    /// wire, it lives as long as the app is open, and it is replaced entire by the next
    /// answer. Nothing is stored on the cashier's computer, so there is nothing on that
    /// computer that could ever be shown instead.
    /// </summary>
    public static void TakeFromTheServer(IEnumerable<Product> fromTheShop)
    {
        _products = fromTheShop.ToList();
        _categories = new List<string> { "All" };
        _categories.AddRange(_products.Select(p => p.Category)
                                      .Where(c => c.Length > 0)
                                      .Distinct(StringComparer.CurrentCultureIgnoreCase)
                                      .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase));
    }

    /// <summary>Empty shelves: a till with no answer from its shop has nothing to sell.</summary>
    public static void NothingToSell()
    {
        _products = new List<Product>();
        _categories = new List<string> { "All" };
    }

    /// <summary>Re-reads the catalogue after it has been edited.</summary>
    public static void Reload()
    {
        // A till's catalogue is not in this database and cannot be re-read from it. Everything
        // that edits a product on a till goes to the server, and the server's next answer is
        // what changes what is on screen.
        if (BelongsToAServer)
        {
            _products ??= new List<Product>();
            _categories ??= new List<string> { "All" };
            return;
        }

        _products = ProductRepository.GetAll();
        _categories = new List<string> { "All" };
        _categories.AddRange(ProductRepository.GetCategories());
    }

    public static IReadOnlyList<Product> Products =>
        _products ?? throw new InvalidOperationException("Catalog.Load() must run at startup.");

    public static IReadOnlyList<string> Categories =>
        _categories ?? throw new InvalidOperationException("Catalog.Load() must run at startup.");

    public static Product? FindByBarcode(string barcode) =>
        Products.FirstOrDefault(p => p.Barcode == barcode.Trim());
}
