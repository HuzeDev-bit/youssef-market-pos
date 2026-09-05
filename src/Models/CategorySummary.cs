namespace MarketPos.Models;

/// <summary>
/// A category tile on the Products page: just the picture and the name, which is all a
/// cashier needs to pick one quickly.
/// </summary>
public sealed class CategorySummary
{
    public required string Name { get; init; }

    /// <summary>
    /// Borrowed from a product inside the category — there is no separate artwork per
    /// category, and a representative item reads far better than a generic icon.
    /// </summary>
    public string? ImagePath { get; init; }
}
