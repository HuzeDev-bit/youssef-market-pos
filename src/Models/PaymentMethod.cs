namespace MarketPos.Models;

/// <summary>
/// How a sale was settled.
///
/// Other covers the ways a Moroccan corner shop actually gets paid that are neither notes
/// nor a bank card — a mobile transfer, a voucher, or the neighbour's tab. It is one bucket
/// on purpose: splitting it further would be guessing at what the shop needs.
/// </summary>
public enum PaymentMethod
{
    Cash,
    Card,
    Other
}
