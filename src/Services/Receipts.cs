using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>
/// Where a printed ticket comes from.
///
/// <para>
/// One question — "what did ticket 412 say?" — asked from three places: the confirmation after
/// a sale, the ticket list, and a reprint. On the shop's own machine the answer is in the
/// database. On a cashier's machine there is no database to ask, and the ticket may not even
/// have been rung up on that counter, so the shop is asked instead.
/// </para>
///
/// <para>
/// Null means no ticket, and the caller cannot tell whether that is because the shop has no
/// such sale or because the shop could not be reached — which is why the till says so out of
/// <see cref="ShopLink.LastProblem"/> rather than guessing. What it never does is print a
/// receipt out of something remembered locally.
/// </para>
/// </summary>
public static class Receipts
{
    /// <summary>One ticket, whole. Null when there is none, or when the shop cannot be asked.</summary>
    public static Receipt? Find(int invoiceNumber)
    {
        if (!Catalog.BelongsToAServer) return SaleRepository.FindByInvoiceNumber(invoiceNumber);

        var ticket = ShopLink.Now(() => ShopLink.Ticket(invoiceNumber));
        return ticket is null ? null : AsReceipt(ticket);
    }

    /// <summary>The last few ticket numbers, for the reprint screen to offer.</summary>
    public static IReadOnlyList<int> Recent()
    {
        if (!Catalog.BelongsToAServer) return SaleRepository.RecentInvoiceNumbers();

        return ShopLink.Now(() => ShopLink.Tickets(null))?.RecentInvoiceNumbers ?? Array.Empty<int>();
    }

    private static Receipt AsReceipt(Link.TicketDetail ticket) => new()
    {
        InvoiceNumber = ticket.InvoiceNumber,
        SoldAt = ticket.SoldAt,
        Lines = ticket.Lines.Select(l => new ReceiptLine
        {
            Name = l.Name,
            Quantity = l.Quantity,
            Unit = l.Unit == nameof(Unit.Kg) ? Unit.Kg : Unit.Each,
            UnitPrice = l.UnitPrice,
            LineTotal = l.LineTotal,
        }).ToList(),
        GrossBeforeDiscount = ticket.GrossBeforeDiscount,
        DiscountKind = Enum.TryParse<DiscountKind>(ticket.DiscountKind, out var kind)
            ? kind
            : DiscountKind.None,
        DiscountValue = ticket.DiscountValue,
        DiscountAmount = ticket.DiscountAmount,
        Subtotal = ticket.Subtotal,
        Tax = ticket.Tax,
        Total = ticket.Total,
        PaymentMethod = Enum.TryParse<PaymentMethod>(ticket.PaymentMethod, out var how)
            ? how
            : PaymentMethod.Cash,
        AmountTendered = ticket.AmountTendered,
        ChangeGiven = ticket.ChangeGiven,
    };
}
