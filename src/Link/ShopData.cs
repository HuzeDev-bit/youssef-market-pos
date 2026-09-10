using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// What the shop's server answers a till with, in one place.
///
/// <para>
/// Two programs serve tills: the standalone server on the shop's own machine, and the
/// all-in-one, which is the same shop with a counter attached. Both must answer a till
/// identically — a till that finds one of them and gets a different answer than it would from
/// the other is a till that behaves differently depending on which machine the shop happened
/// to start. So the answers are written once, here, and each server maps its routes onto them.
/// </para>
///
/// <para>
/// Everything in this class runs on the machine that owns the database. Nothing in it is ever
/// executed by a cashier's copy.
/// </para>
/// </summary>
public static class ShopData
{
    // ---------------------------------------------------------------- what is this worth

    /// <summary>
    /// Answers "what is this and what does it cost" for a scan or a typed name.
    ///
    /// The cost travels only when the till says the owner is asking. A cashier who can read
    /// the purchase price of everything on the shelf knows the shop's margins, and that is the
    /// owner's business — so the decision is made here, on the shop's own machine, rather than
    /// left to a client to be trusted about.
    /// </summary>
    public static PriceAnswer PriceCheck(string query, bool forTheOwner)
    {
        query = (query ?? string.Empty).Trim();

        var scanned = query.Length > 0 ? StockRepository.FindByBarcode(query) : null;
        var matches = 0;

        if (scanned is null && query.Length > 0)
        {
            var found = StockRepository.List(search: query);
            matches = found.Count;
            if (found.Count == 1) scanned = found[0];
        }
        else if (scanned is not null)
        {
            matches = 1;
        }

        if (scanned is null)
            return new PriceAnswer(query, false, matches, 0, query, string.Empty, null,
                                   string.Empty, 0m, 0m, nameof(Unit.Each), 0m, false);

        return new PriceAnswer(
            query, true, matches, scanned.Id, scanned.Name, scanned.Category,
            scanned.Barcode.Length > 0 ? scanned.Barcode : null,
            scanned.Shelf, scanned.Price,
            forTheOwner ? scanned.Cost : 0m,
            scanned.Unit.ToString(), scanned.Stock, scanned.IsActive);
    }

    // ---------------------------------------------------------------- what was sold

    /// <summary>The till's ticket list, today's takings, and the numbers a reprint offers.</summary>
    public static TicketList Tickets(string? search)
    {
        var sales = SaleRepository.ListSales(search);
        var (count, total) = SaleRepository.DayTotals(DateTime.Now);

        return new TicketList(
            sales.Select(s => new TicketSummary(
                s.InvoiceNumber, s.SoldAt, s.Total, s.DiscountAmount,
                s.PaymentMethod.ToString(), s.LineCount)).ToList(),
            count,
            total,
            SaleRepository.RecentInvoiceNumbers());
    }

    /// <summary>One ticket, whole, for printing or reprinting. Null when there is no such sale.</summary>
    public static TicketDetail? Ticket(int invoiceNumber)
    {
        if (SaleRepository.FindByInvoiceNumber(invoiceNumber) is not { } receipt) return null;

        return new TicketDetail(
            receipt.InvoiceNumber,
            receipt.SoldAt,
            receipt.Lines.Select(l => new TicketLine(
                l.Name, l.Quantity, l.Unit.ToString(), l.UnitPrice, l.LineTotal)).ToList(),
            receipt.GrossBeforeDiscount,
            receipt.DiscountKind.ToString(),
            receipt.DiscountValue,
            receipt.DiscountAmount,
            receipt.Subtotal,
            receipt.Tax,
            receipt.Total,
            receipt.PaymentMethod.ToString(),
            receipt.AmountTendered,
            receipt.ChangeGiven);
    }

    // ---------------------------------------------------------------- how the shop is filed

    /// <summary>The shop's categories, for a till filling in a product it has just scanned.</summary>
    public static IReadOnlyList<CategoryName> Categories() =>
        CategoryRepository.List().Select(c => new CategoryName(c.Id, c.Name)).ToList();

    /// <summary>The shop's suppliers, for the same form.</summary>
    public static IReadOnlyList<SupplierName> Suppliers() =>
        SupplierRepository.List().Select(s => new SupplierName(s.Id, s.Name)).ToList();

    // ---------------------------------------------------------------- what the shop is

    /// <summary>The settings every till shares, read from the shop's own database.</summary>
    public static ShopWideSettings Settings() => new(
        AppSettings.Current.BusinessName,
        AppSettings.Current.BusinessAddress,
        AppSettings.Current.BusinessPhone,
        AppSettings.Current.TaxId,
        AppSettings.Current.Currency,
        AppSettings.Current.ReceiptFooter,
        AppSettings.Current.DefaultLowStock);
}
