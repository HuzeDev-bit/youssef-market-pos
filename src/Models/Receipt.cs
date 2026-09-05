namespace MarketPos.Models;

/// <summary>
/// A completed sale, read back from the database for printing. Everything here is a
/// snapshot of what was actually charged — reprinting only ever reads this.
/// </summary>
public sealed class Receipt
{
    public required int InvoiceNumber { get; init; }
    public required DateTime SoldAt { get; init; }
    public required IReadOnlyList<ReceiptLine> Lines { get; init; }

    public required decimal GrossBeforeDiscount { get; init; }
    public required DiscountKind DiscountKind { get; init; }
    public required decimal DiscountValue { get; init; }
    public required decimal DiscountAmount { get; init; }

    public required decimal Subtotal { get; init; }
    public required decimal Tax { get; init; }
    public required decimal Total { get; init; }

    public required PaymentMethod PaymentMethod { get; init; }
    public required decimal AmountTendered { get; init; }
    public required decimal ChangeGiven { get; init; }

    /// <summary>"Remise (10%)" or "Remise (10.00 DH)" — empty when no discount was given.</summary>
    public string DiscountLabel => DiscountKind switch
    {
        DiscountKind.Percent => $"Remise ({DiscountValue:0.##}%)",
        DiscountKind.Fixed => $"Remise ({DiscountValue:0.00} DH)",
        _ => string.Empty,
    };

    public bool HasDiscount => DiscountKind != DiscountKind.None && DiscountAmount > 0;
}

public sealed class ReceiptLine
{
    public required string Name { get; init; }
    public required decimal Quantity { get; init; }
    public required Unit Unit { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal LineTotal { get; init; }

    public string QuantityLabel => Unit == Unit.Kg ? $"{Quantity:0.###} kg" : $"{Quantity:0}";
}
