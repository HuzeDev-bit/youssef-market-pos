namespace MarketPos.Models;

/// <summary>
/// One line of a sale as the database needs it: a product and how much of it.
///
/// The repository used to take the till's own cart line, which meant the data layer depended
/// on a view model — so a sale could only be written by something with a screen. It cannot
/// stay that way once a server saves sales too, and it was the wrong direction anyway: what a
/// sale is made of is a fact about the shop, not about the till's UI.
/// </summary>
public sealed record SaleItem(Product Product, decimal Quantity)
{
    /// <summary>
    /// What this line comes to. Weighed goods are rounded to the centime here rather than
    /// carried at full precision, so the line the customer is charged is the line stored.
    /// </summary>
    public decimal LineTotal => Math.Round(Product.Price * Quantity, 2);
}
