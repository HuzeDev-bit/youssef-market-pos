using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>
/// What language the shop is in.
///
/// Keyed by the English text itself rather than by invented names like <c>btn_save_product</c>.
/// Two reasons, and the second is the one that matters. The obvious one is that nobody has to
/// keep a key list in their head. The real one is that a missing translation falls back to
/// readable English instead of to a naked identifier: a shop that meets a screen nobody got to
/// yet reads "Hold ticket", not "till.hold.ticket".
///
/// Applied when a window is built, not while it is open. Live re-translation of a running till
/// means every label in the app becomes a binding to a dictionary lookup, and the failure mode
/// — a cashier halfway through a sale watching the screen change language — is worse than
/// asking for a restart on the one day somebody sets this.
/// </summary>
public static class Loc
{
    /// <summary>The language every window is built in. Read once at start-up.</summary>
    public static Language Current { get; private set; } = Language.English;

    /// <summary>True when the interface has to be laid out right to left.</summary>
    public static bool IsRightToLeft => Current == Language.Arabic;

    /// <summary>
    /// Picks the language up from settings. Called once, before the first window exists —
    /// changing it afterwards is a restart, which is what <see cref="AppSettings"/> stores.
    /// </summary>
    public static void Load()
    {
        Current = AppSettings.Current.Language switch
        {
            "fr" => Language.French,
            "ar" => Language.Arabic,
            _ => Language.English,
        };

        ApplyCulture();
    }

    /// <summary>For the diagnostics, which render every screen in every language.</summary>
    public static void Use(Language language)
    {
        Current = language;
        ApplyCulture();
    }

    /// <summary>
    /// Makes Windows write dates in the shop's language while leaving numbers alone.
    ///
    /// Both halves matter. "29 Aug" in an Arabic shop is the app failing to finish the job;
    /// but taking the culture wholesale would also switch the decimal separator, and a till
    /// that starts writing "8,50" into a database of "8.50" has done something far worse than
    /// print a month in the wrong language.
    /// </summary>
    private static void ApplyCulture()
    {
        var name = Current switch
        {
            Language.French => "fr-MA",
            Language.Arabic => "ar-MA",
            _ => "en-GB",
        };

        try
        {
            var culture = (System.Globalization.CultureInfo)
                System.Globalization.CultureInfo.GetCultureInfo(name).Clone();

            // Dates from the language; numbers from nowhere at all.
            culture.NumberFormat = System.Globalization.CultureInfo.InvariantCulture.NumberFormat;

            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            // A machine without that culture installed keeps English dates. Not worth a crash.
        }
    }

    public static string Code(Language language) => language switch
    {
        Language.French => "fr",
        Language.Arabic => "ar",
        _ => "en",
    };

    /// <summary>What the language calls itself. Never translated — a list of languages is read
    /// by somebody who does not yet have the app in theirs.</summary>
    public static string NativeName(Language language) => language switch
    {
        Language.French => "Français",
        Language.Arabic => "العربية",
        _ => "English",
    };

    /// <summary>
    /// The English text in the shop's language, or the English itself when nothing better
    /// exists. Never throws and never returns empty for a non-empty input: a screen with a
    /// blank button on it is worse than a screen with an English one.
    /// </summary>
    /// <summary>
    /// Every phrase asked for that had no translation, when <see cref="RecordMisses"/> is on.
    ///
    /// Static analysis cannot find these reliably: half the app's sentences are written as two
    /// string literals joined across a line break, and what has to be translated is the joined
    /// result. So the diagnostics draw every screen in French and let the app itself say what
    /// it could not translate.
    /// </summary>
    public static readonly SortedSet<string> Misses = new(StringComparer.Ordinal);

    public static bool RecordMisses { get; set; }

    public static string T(string english)
    {
        if (Current == Language.English || string.IsNullOrEmpty(english)) return english;

        // A label laid out with spaces inside its attribute — Text=" bought · " — is the same
        // phrase as the one in the table. Translate the words and give the padding back, or
        // one stray space is the difference between a translated screen and an English one.
        var trimmed = english.Trim();
        if (trimmed.Length != english.Length && Translations.Table.ContainsKey(trimmed))
        {
            var lead = english[..(english.Length - english.TrimStart().Length)];
            var trail = english[english.TrimEnd().Length..];
            return lead + T(trimmed) + trail;
        }

        if (RecordMisses && !Translations.Table.ContainsKey(english)
            && english.Any(char.IsLetter))
        {
            lock (Misses) Misses.Add(english);
        }

        return Translations.Table.TryGetValue(english, out var row)
            ? (Current == Language.French ? row.Fr : row.Ar) is { Length: > 0 } translated
                ? translated
                : english
            : english;
    }

    /// <summary>
    /// A run of Latin text — an amount, a percentage, a barcode — pinned so an Arabic
    /// paragraph cannot take it apart.
    ///
    /// "434.00 DH" is a run of Latin characters. Dropped into a right-to-left paragraph, the
    /// bidirectional algorithm reorders the number and the currency as two separate runs and
    /// the shop reads "DH 434.00" — and a minus sign lands after the figure, which on a loss
    /// is worse than untidy. The marks below pin the whole amount as one left-to-right run
    /// without changing where the column sits.
    ///
    /// Costs nothing outside Arabic: in English and French the marks are not added at all.
    /// </summary>
    /// <remarks>
    /// Dates are handled elsewhere, by <see cref="Culture"/>: a month name is words, not a run
    /// of digits, and belongs to the language rather than to the layout.
    /// </remarks>
    public static string Ltr(string run) =>
        IsRightToLeft ? "‎" + run + "‎" : run;

    /// <summary>
    /// A translated line with something of the shop's own in it — a name, a number, a total.
    /// The placeholders stay in the translated text, so a French line can put them in a
    /// different order from the English one.
    /// </summary>
    public static string T(string english, params object[] values)
    {
        try { return string.Format(T(english), values); }
        catch (FormatException) { return string.Format(english, values); }
    }
}
