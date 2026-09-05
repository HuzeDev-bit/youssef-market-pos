namespace MarketPos.Models;

/// <summary>How a remise was expressed by the cashier. Stored so the receipt can show "Remise (10%)".</summary>
public enum DiscountKind
{
    None,
    Percent,
    Fixed
}
