namespace MarketPos.Services;

/// <summary>
/// One panel of letters, in one language.
///
/// Three of them, because the shop types in three: product names in Arabic, supplier and
/// wholesaler names in French, barcodes and imported brands in English. A single layout would
/// have meant the owner switching the whole machine's keyboard language to type one word.
///
/// Each row is written in the order the keys sit on a real keyboard, left to right, one
/// character per key. Digits are not here — they are on every layout, always, because most of
/// what gets typed into a till is a number.
/// </summary>
public sealed record KeyboardLayout(string Code, string Name, string[] Rows)
{
    /// <summary>Standard Arabic 101, which is the layout on every keyboard sold in Morocco.</summary>
    public static readonly KeyboardLayout Arabic = new("ar", "عربي",
    [
        "ضصثقفغعهخحج",
        "شسيبلاتنمكط",
        "ئءؤرىةوزظ",
    ]);

    /// <summary>AZERTY, not QWERTY: French keyboards are what the shop has on the counter.</summary>
    public static readonly KeyboardLayout French = new("fr", "FR",
    [
        "azertyuiop",
        "qsdfghjklm",
        "wxcvbn",
    ]);

    public static readonly KeyboardLayout English = new("en", "EN",
    [
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
    ]);

    public static readonly IReadOnlyList<KeyboardLayout> All = [Arabic, French, English];

    /// <summary>The digits row, above the letters on every layout.</summary>
    public const string Digits = "1234567890";

    /// <summary>The handful of marks a shop actually types: prices, sizes, notes.</summary>
    public const string Marks = ".,-/%";

    /// <summary>Arabic has no upper case, so the shift key has nothing to offer on it.</summary>
    public bool HasCase => Code != "ar";

    /// <summary>
    /// The layout to open on, which is the one the interface is already in. A shop running in
    /// Arabic types Arabic far more often than anything else, and making that the first thing
    /// on screen saves a press on almost every word.
    /// </summary>
    public static KeyboardLayout ForTheShop() =>
        All.FirstOrDefault(l => l.Code == Loc.Code(Loc.Current)) ?? Arabic;
}
