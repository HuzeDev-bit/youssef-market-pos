using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>
/// Thrown when a sale would take more off the shelf than the shelf holds.
///
/// Its own type rather than a plain message, because every layer above has to be able to tell
/// this apart from a real failure: the till turns it into a line the cashier can act on, and
/// the back office refuses the movement without pretending the database is broken.
/// </summary>
public sealed class NotEnoughStockException : InvalidOperationException
{
    public NotEnoughStockException(string productName, decimal available, decimal wanted)
        : base(available <= 0m
            ? Loc.T("Error: {0} is out of stock.", productName)
            : Loc.T("Error: only {0} of {1} left.", $"{available:0.###}", productName))
    {
        ProductName = productName;
        Available = available;
        Wanted = wanted;
    }

    /// <summary>
    /// The same refusal, in the shop's own words, when it arrives from the shop's own machine
    /// rather than being worked out here. A till has no shelf to count; what it has is the
    /// server's answer, and this keeps that answer the same type every screen already catches.
    /// </summary>
    public NotEnoughStockException(string message)
        : base(message)
    {
        ProductName = string.Empty;
    }

    public string ProductName { get; }

    /// <summary>What the shelf actually holds. Never negative.</summary>
    public decimal Available { get; }

    /// <summary>What was asked for.</summary>
    public decimal Wanted { get; }
}
