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
    /// False for something the shop keeps a record of but does not sell over the counter.
    /// It disappears from the till entirely — it cannot be pressed and it cannot be scanned.
    /// </summary>
    public bool SoldAtTheTill { get; init; } = true;

    /// <summary>
    /// True when this is something a scanner can read: a manufacturer's barcode, printed on
    /// the packet.
    ///
    /// The shop's own codes are in the 2xxxxxxxxxxx range, which EAN-13 reserves for in-store
    /// use. A product wearing one of those has no barcode of its own — it is bread, or loose
    /// tomatoes, and the code exists only so the database has a key. That is exactly the split
    /// the till needs: scannable things are scanned, and everything else needs a picture to
    /// press.
    /// </summary>
    public bool IsScannable => Barcode.Length > 0 && !IsShopsOwnCode(Barcode);

    /// <summary>
    /// An in-store code minted by <c>StockRepository.NextInternalBarcode</c>: thirteen digits
    /// beginning with 2. Kept here rather than in the repository because the till has to make
    /// the same judgement about a product it is only holding in memory.
    /// </summary>
    public static bool IsShopsOwnCode(string barcode) =>
        barcode.Length == 13 && barcode[0] == '2' && barcode.All(char.IsAsciiDigit);

    /// <summary>Shelf-price label for the product tile, e.g. "6.90 DH/kg" or "8.50 DH".</summary>
    public string PriceLabel => Services.Loc.Ltr(Unit == Unit.Kg
        ? $"{Price.ToString("0.00")} DH/kg"
        : $"{Price.ToString("0.00")} DH");
}
