namespace MarketPos.Link;

/// <summary>
/// What a till asks the shop for beyond the catalogue and the sale.
///
/// <para>
/// A cashier's machine holds no books, so everything on its screen has to be asked for: what
/// a scanned thing costs, what was sold today, what a ticket said when it was printed, what
/// the shop calls itself. These are the shapes those answers travel in.
/// </para>
///
/// <para>
/// Deliberately flat and deliberately not the domain types. A till needs to draw these, not to
/// reason about them, and a shape that crosses a wire is a promise — a field is added rather
/// than changed, and nothing here is renamed casually.
/// </para>
/// </summary>
public static class TillContracts
{
    public const int Version = 1;
}

/// <summary>The answer to "what is this and what does it cost", without touching the sale.</summary>
public sealed record PriceAnswer(
    string Query,
    bool Found,
    int Matches,
    int ProductId,
    string Name,
    string Category,
    string? Barcode,
    string Shelf,
    decimal Price,
    decimal Cost,
    string Unit,
    decimal Stock,
    bool IsActive);

/// <summary>One ticket as the till's list draws it.</summary>
public sealed record TicketSummary(
    int InvoiceNumber,
    DateTime SoldAt,
    decimal Total,
    decimal DiscountAmount,
    string PaymentMethod,
    int LineCount);

/// <summary>Today's takings, for the line under the ticket list.</summary>
public sealed record TicketList(
    IReadOnlyList<TicketSummary> Tickets,
    int TodayCount,
    decimal TodayTotal,
    IReadOnlyList<int> RecentInvoiceNumbers);

/// <summary>One line of a ticket, exactly as it was rung up.</summary>
public sealed record TicketLine(
    string Name,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>
/// A whole ticket, for printing and for reprinting.
///
/// Everything needed to put it on paper travels with it, including the shop's own details:
/// the till has no database to look them up in, and a receipt printed with the wrong shop
/// name on it is worse than one not printed at all.
/// </summary>
public sealed record TicketDetail(
    int InvoiceNumber,
    DateTime SoldAt,
    IReadOnlyList<TicketLine> Lines,
    decimal GrossBeforeDiscount,
    string DiscountKind,
    decimal DiscountValue,
    decimal DiscountAmount,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    string PaymentMethod,
    decimal AmountTendered,
    decimal ChangeGiven);

/// <summary>One category, as a till's dropdown needs it.</summary>
public sealed record CategoryName(int Id, string Name);

/// <summary>One supplier, as a till's dropdown needs it.</summary>
public sealed record SupplierName(int Id, string Name);

/// <summary>The settings that belong to the shop rather than to a computer.</summary>
public sealed record ShopWideSettings(
    string BusinessName,
    string BusinessAddress,
    string BusinessPhone,
    string TaxId,
    string Currency,
    string ReceiptFooter,
    decimal DefaultLowStock);
