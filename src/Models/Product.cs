namespace MarketPos.Models;

/// <summary>
/// A sellable item. Price is the shelf price in MAD, tax-inclusive (as displayed
/// to shoppers in Morocco) — TaxRate is only used to split the receipt into
/// HT/TVA/TTC lines, it is not added on top at checkout.
/// </summary>
public sealed class Product
{
    /// <summary>Database row id. 0 for a product that has not been saved yet.</summary>
    public int Id { get; init; }

    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required decimal Price { get; init; }
    public Unit Unit { get; init; } = Unit.Each;

    /// <summary>Moroccan VAT bracket for this item: 0, 0.07, 0.10 or 0.20.</summary>
    public decimal TaxRate { get; init; } = 0.20m;

    /// <summary>
    /// Placeholder artwork for the product tile — drawn from a font, so it needs no image
    /// files and works offline. Shown only while <see cref="ImagePath"/> is empty.
    /// </summary>

    /// <summary>
    /// Real product photo, once the client supplies them. Any path WPF can load
    /// (absolute, or pack:// for embedded). Takes over the tile when set.
    /// </summary>
    public string? ImagePath { get; init; }

    /// <summary>
    /// How many are on the shelf.
    ///
    /// The till carries it so a scan can be refused at the counter rather than at the moment
    /// the sale is saved. The database still has the last word — see
    /// <c>InventoryRepository.Move</c> — because this figure is a snapshot and a second till
    /// may have sold the last one a moment ago.
    /// </summary>
    public decimal Stock { get; init; }

    /// <summary>Nothing left on the shelf.</summary>
    public bool IsOutOfStock => Stock <= 0m;

    /// <summary>
    /// The badge on an empty tile, in the shop's language.
    ///
    /// A property rather than a label typed into the tile's template. The template is built
    /// once per product as the grid scrolls, and whether a translator has run by then depends
    /// on load order — which is a thing that works on one machine and not another. Asking the
    /// product what it is called takes the timing out of it.
    /// </summary>
    public string OutOfStockLabel => Services.Loc.T("Out of stock");

    /// <summary>
    /// False for something the shop keeps a record of but does not sell over the counter.
    /// It disappears from the till entirely — it cannot be pressed and it cannot be scanned.
    /// </summary>
    public bool SoldAtTheTill { get; init; } = true;

    /// <summary>
    /// True when this is something a scanner can read: a manufacturer's barcode, printed on
    /// the packet.
    ///
    /// A product without one has no barcode at all — the column is NULL in the database, and
    /// empty here. It is bread, or loose tomatoes, and it is found by pressing its picture
    /// rather than by scanning. That is exactly the split the till needs, and it is now the
    /// plain reading of the data rather than a range of digits that had to be decoded.
    ///
    /// <para>
    /// The shop used to mint a code in the 2xxxxxxxxxxx range for these, because the column
    /// could not be empty. It can now, so nothing is invented: a product either carries a
    /// barcode somebody printed on it or it carries none.
    /// </para>
    /// </summary>
    public bool IsScannable => Barcode.Length > 0;

    /// <summary>Shelf-price label for the product tile, e.g. "6.90 DH/kg" or "8.50 DH".</summary>
    public string PriceLabel => Services.Loc.Ltr(Unit == Unit.Kg
        ? $"{Price.ToString("0.00")} DH/kg"
        : $"{Price.ToString("0.00")} DH");
}
