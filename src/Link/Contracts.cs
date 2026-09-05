namespace MarketPos.Link;

/// <summary>
/// What a till and the server say to each other.
///
/// Lives in the shared project because both ends have to agree on it exactly. It sat in the
/// server before, which meant the till could not name the shapes it was sending without
/// referencing a web application.
///
/// Deliberately small and deliberately not the domain types. A till needs the catalogue, a way
/// to sign in, and a way to hand over sales — that is all. Everything else the back office
/// does happens on this machine, against the database directly, so it never crosses a wire.
///
/// These shapes are a promise: a till in the shop may be an older build than the server after
/// an update, so a field is added rather than changed, and nothing here is renamed casually.
/// </summary>
public static class Contracts
{
    /// <summary>Bumped when a field changes meaning. A till refuses a server it does not know.</summary>
    public const int Version = 1;
}

/// <summary>What the server is and whether it is willing to talk.</summary>
public sealed record Hello(string Shop, int Version, string ServerId, DateTime Now);

/// <summary>A till proving who is at it. The password is checked here, never on the till.</summary>
public sealed record SignInRequest(int WorkerId, string Password);

public sealed record SignedIn(int WorkerId, string Name, string Role);

/// <summary>
/// One member of staff as a till needs them: enough to put their name on the sign-in list and
/// to check their password without asking the server.
///
/// The password hash travels. It has to: a till that could only sign a cashier in while the
/// network was up would lock the shop out of its own counter the moment somebody unplugged the
/// back office — which is the exact situation the rest of this design exists to survive. What
/// travels is a PBKDF2-SHA256 hash with its own salt, never a password, and only for staff the
/// owner has actually given one to.
/// </summary>
public sealed record StaffMember(
    int Id,
    string Name,
    string Role,
    string PinHash,
    string PinSalt,
    bool IsActive);

/// <summary>One product as a till needs it — enough to ring it up and print it on a receipt.</summary>
public sealed record CatalogItem(
    int Id,
    string Barcode,
    string Name,
    string Category,
    decimal Price,
    decimal TaxRate,
    string Unit,
    decimal Stock);

/// <summary>
/// The catalogue, with a stamp the till sends back next time.
///
/// The till keeps its own copy so it can sell while the server is off, and asks only for what
/// changed since it last looked. On a shop with a few hundred products that is the difference
/// between a moment and a wait every time the till starts.
/// </summary>
public sealed record CatalogPage(string Stamp, IReadOnlyList<CatalogItem> Items, bool Complete);

/// <summary>One line of a sale a till is handing over.</summary>
public sealed record SaleLineDto(
    int ProductId,
    string Barcode,
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRate,
    string Unit);

/// <summary>
/// A sale that happened at a till.
///
/// <paramref name="TillReference"/> is the till's own id for it and is what makes handing a
/// sale over safe to retry: the till cannot know whether a request that timed out was written,
/// so it sends the same one again and the server recognises it rather than taking the money
/// twice.
/// </summary>
public sealed record SaleUpload(
    string TillReference,
    DateTime SoldAt,
    int? WorkerId,
    string WorkerName,
    string PaymentMethod,
    decimal AmountTendered,
    decimal GrossBeforeDiscount,
    string DiscountKind,
    decimal DiscountValue,
    decimal DiscountAmount,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    IReadOnlyList<SaleLineDto> Lines);

/// <summary>
/// What the server did with it. <paramref name="AlreadyHad"/> is true when the sale was
/// recognised from a previous attempt, so the till can stop retrying without double-counting.
/// </summary>
public sealed record SaleAccepted(string TillReference, int InvoiceNumber, bool AlreadyHad);

/// <summary>A batch handed over at once, because a till that has been offline has several.</summary>
public sealed record SaleBatch(IReadOnlyList<SaleUpload> Sales);

public sealed record SaleBatchResult(IReadOnlyList<SaleAccepted> Accepted, IReadOnlyList<string> Rejected);
