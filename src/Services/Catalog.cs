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

    /// <summary>Re-reads the catalogue after it has been edited.</summary>
    public static void Reload()
    {
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
