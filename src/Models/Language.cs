namespace MarketPos.Models;

/// <summary>
/// The languages the shop can be run in.
///
/// Three, chosen for one counter in Morocco: the owner's French, a customer-facing Arabic, and
/// the English the software was written in. Adding a fourth is a column in the translation
/// table and nothing else.
/// </summary>
public enum Language
{
    English,
    French,
    Arabic,
}
