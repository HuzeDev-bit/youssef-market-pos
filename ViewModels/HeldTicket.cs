using System.Globalization;

namespace MarketPos.ViewModels;

/// <summary>
/// A sale parked mid-scan so the till can serve someone else — the customer who went
/// back for eggs. Holds the live CartLine objects, so resuming restores quantities,
/// weights and all exactly as they were.
/// </summary>
public sealed class HeldTicket
{
    public int Number { get; }
    public IReadOnlyList<CartLine> Lines { get; }
    public DateTime HeldAt { get; }

    public HeldTicket(int number, IReadOnlyList<CartLine> lines)
    {
        Number = number;
        Lines = lines;
        HeldAt = DateTime.Now;
    }

    public decimal Total => Math.Round(Lines.Sum(l => l.LineTotal), 2);

    /// <summary>Deliberately NOT "Ticket": receipts use that word, and a cashier
    /// typing a hold number into Reprint finds nothing and assumes it is broken.</summary>
    public string Label => $"Hold {Number}";

    /// <summary>"Milk 1L +2 · 25.40 DH" — enough for the cashier to tell tickets apart at a glance.</summary>
    public string Summary
    {
        get
        {
            var first = Lines.Count > 0 ? Lines[0].Product.Name : "Empty";
            var extra = Lines.Count - 1;
            var items = extra > 0 ? $"{first} +{extra}" : first;
            return $"{items}  ·  {Total.ToString("N2", CultureInfo.InvariantCulture)} DH";
        }
    }

    public string HeldAtLabel => $"Held at {HeldAt:HH:mm}";
}
