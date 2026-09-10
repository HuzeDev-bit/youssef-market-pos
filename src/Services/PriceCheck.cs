using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>
/// Answers "what is this and what does it cost" from a barcode, without touching the sale.
///
/// The question a shopkeeper asks a hundred times a day: a customer holds something up, the
/// shelf label has fallen off, and the answer is in the database already. Doing it by ringing
/// the item up and then voiding the line is how a till ends up with phantom sales in it, so
/// this path never writes anything and never touches the cart.
///
/// What it shows depends on who is asking. The shelf price is for anyone standing at the till.
/// What the shop paid for it is not: a cashier who can read the purchase price of everything
/// on the shelf knows the shop's margins, and that is the owner's business. So cost comes out
/// only for <see cref="Permission.SeeFinancials"/>, which is the owner alone.
/// </summary>
public sealed class PriceCheck
{
    /// <summary>What was scanned or typed.</summary>
    public required string Query { get; init; }

    /// <summary>The product, or null when the shop does not sell this.</summary>
    public StockItem? Item { get; init; }

    /// <summary>How many products the text matched. More than one means it was a name, not a code.</summary>
    public int Matches { get; init; }

    /// <summary>
    /// True when the shop could not be asked at all — as opposed to being asked and answering
    /// that it does not sell this. A till must never quote a price it made up locally, and it
    /// must never tell a customer the shop has no such thing when the truth is that the wire
    /// is down.
    /// </summary>
    public bool Unreachable { get; init; }

    public bool Found => Item is not null;

    /// <summary>True when the text looked like a scan rather than someone typing a word.</summary>
    public bool WasScanned => Query.Length >= 6 && Query.All(char.IsDigit);

    public string Name => Item?.Name ?? Query;

    /// <summary>Where it sits and what it is called by the scanner, on one quiet line.</summary>
    public string Detail
    {
        get
        {
            if (Item is null) return string.Empty;

            var parts = new List<string>();
            if (Item.Category.Length > 0) parts.Add(Item.Category);
            if (Item.Barcode.Length > 0) parts.Add(Item.Barcode);
            if (Item.Shelf.Length > 0) parts.Add($"shelf {Item.Shelf}");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The figure the customer is waiting for.</summary>
    public string PriceText => Item is null
        ? string.Empty
        : Loc.Ltr(Item.Unit == Unit.Kg ? $"{Item.Price:N2} DH/kg" : $"{Item.Price:N2} DH");

    /// <summary>
    /// How many are left, in the words a shopkeeper uses. "0" is a number; "none left on the
    /// shelf" is an answer.
    /// </summary>
    public string StockText
    {
        get
        {
            if (Item is null) return string.Empty;

            var unit = Item.Unit == Unit.Kg ? "kg" : Item.Stock == 1m ? "left" : "left";
            return Item.Status switch
            {
                StockStatus.OutOfStock => Loc.T("none left on the shelf"),
                StockStatus.LowStock when Item.MinStock > 0m =>
                    $"{Item.Stock:0.###} {unit} — below the {Item.MinStock:0.###} you asked for",
                _ => $"{Item.Stock:0.###} {unit}",
            };
        }
    }

    /// <summary>Owner only. Empty for everyone else, including in the string itself.</summary>
    public bool ShowsCost => Item is { Cost: > 0m } && Session.Can(Permission.SeeFinancials);

    public string CostText => !ShowsCost || Item is null
        ? string.Empty
        : Loc.T("cost you {0} · you keep {1} ({2}%)",
                Loc.Ltr($"{Item.Cost:N2} DH"), Loc.Ltr($"{Item.Margin:N2} DH"),
                Loc.Ltr($"{Item.MarginPercent:0.#}"));

    /// <summary>What to say when there is nothing to show.</summary>
    public string MissText => Unreachable
        ? Loc.T("Cannot reach the shop's server, so there is no price to show. {0}",
                ShopLink.LastProblem)
        : Matches > 1
        ? Loc.T("{0} products match “{1}” — scan it, or type more of the name", Matches, Query)
        : WasScanned
            ? Loc.T("The shop does not sell this yet. Add it in the back office and it will scan next time.")
            : Loc.T("Nothing here is called “{0}”", Query);

    /// <summary>
    /// Resolves a scan or a typed name. Barcode first and exact — a scan must never be
    /// reinterpreted as a search, or the answer given to the customer is about a different
    /// product with a similar name.
    /// </summary>
    public static PriceCheck For(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return new PriceCheck { Query = query, Matches = 0 };

        // A till asks the shop. It has no database of its own to look in, and the answer a
        // customer is waiting for must be the shop's current price rather than whatever this
        // machine last happened to hear.
        if (Catalog.BelongsToAServer) return FromTheShop(query);

        var scanned = StockRepository.FindByBarcode(query);
        if (scanned is not null)
            return new PriceCheck { Query = query, Item = scanned, Matches = 1 };

        // Typed path. One match is an answer; several is a question, and guessing at it would
        // be quoting a price for something the customer is not holding.
        var matches = StockRepository.List(search: query);
        return new PriceCheck
        {
            Query = query,
            Item = matches.Count == 1 ? matches[0] : null,
            Matches = matches.Count,
        };
    }

    /// <summary>
    /// The same question, asked over the wire.
    ///
    /// <para>
    /// The answer is rebuilt into the same shape the screen already binds to, so the price
    /// card does not know or care which machine answered. What it cannot do is invent one: a
    /// shop that cannot be reached says so, and <see cref="Unreachable"/> is what the till
    /// shows instead of a price. Quoting a customer a price out of a stale local copy is how a
    /// shop sells at last month's price.
    /// </para>
    /// </summary>
    private static PriceCheck FromTheShop(string query)
    {
        var owner = Session.Can(Permission.SeeFinancials);
        var answer = ShopLink.Now(() => ShopLink.PriceCheck(query, owner));

        if (answer is null)
            return new PriceCheck { Query = query, Matches = 0, Unreachable = true };

        if (!answer.Found)
            return new PriceCheck { Query = query, Matches = answer.Matches };

        return new PriceCheck
        {
            Query = query,
            Matches = answer.Matches,
            Item = new Models.StockItem
            {
                Id = answer.ProductId,
                Barcode = answer.Barcode ?? string.Empty,
                Name = answer.Name,
                Category = answer.Category,
                Shelf = answer.Shelf,
                Price = answer.Price,
                Cost = answer.Cost,
                Stock = answer.Stock,
                Unit = answer.Unit == nameof(Models.Unit.Kg) ? Models.Unit.Kg : Models.Unit.Each,
                IsActive = answer.IsActive,
            },
        };
    }
}
