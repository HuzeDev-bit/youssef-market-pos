namespace MarketPos.Models;

/// <summary>
/// The three screens the till has. Anything not on this list was removed deliberately —
/// stock and prices live in the back office, behind a sign-in.
/// </summary>
public enum PageKind
{
    Sale,
    Products,
    Tickets
}
