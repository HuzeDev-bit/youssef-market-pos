using MarketPos.Data;

namespace MarketPos.Services;

/// <summary>
/// The settings that belong to the shop rather than to a computer.
///
/// <para>
/// A shop's name, its currency, what its receipts say and what counts as low stock are facts
/// about the business. They were kept in a JSON file beside the database, which was fine while
/// there was one machine — and wrong the moment there were two, because the cashier's computer
/// then had its own idea of what the shop was called and printed it on receipts.
/// </para>
///
/// <para>
/// So these live in the database, where the shop's other facts live, and every machine reads
/// the same answer. What stays in the file on each machine is what is true only of that
/// machine: which printer is plugged into it, where its server is, what the till is called,
/// and which language the person standing at it reads. Those are not the shop's business and
/// must not travel — a second till would otherwise start printing to a printer that is not
/// there.
/// </para>
///
/// <para>
/// Values are cached after the first read. The dashboard asks for the currency once per figure
/// on the screen, and a database round trip for each of those would be a page that got slower
/// the more it had to say.
/// </para>
/// </summary>
public static class ShopSettings
{
    private static Dictionary<string, string>? _known;
    private static readonly object Lock = new();

    /// <summary>Reads a shop-wide setting, or the given fallback when it has never been set.</summary>
    public static string Get(string key, string fallback)
    {
        try
        {
            lock (Lock)
            {
                _known ??= ReadAll();
                return _known.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;
            }
        }
        catch
        {
            // A shop whose database is briefly unreachable still has a name to print. Falling
            // back is always better than a screen that will not draw.
            return fallback;
        }
    }

    public static decimal GetNumber(string key, decimal fallback) =>
        decimal.TryParse(Get(key, string.Empty),
                         System.Globalization.NumberStyles.Number,
                         System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>Writes a shop-wide setting. Every machine sees it on its next read.</summary>
    public static void Set(string key, string value)
    {
        try
        {
            using var connection = Database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO shop_settings(key, value, updated_at) VALUES($key, $value, $now)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
                """;
            command.With("$key", key)
                   .With("$value", value ?? string.Empty)
                   .WithDate("$now", DateTime.Now);
            command.ExecuteNonQuery();

            lock (Lock)
            {
                _known ??= new Dictionary<string, string>(StringComparer.Ordinal);
                _known[key] = value ?? string.Empty;
            }
        }
        catch
        {
            // Losing a setting is survivable; crashing mid-shift is not.
        }
    }

    public static void SetNumber(string key, decimal value) =>
        Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Forgets what was read, so the next question goes back to the database.</summary>
    public static void Reread()
    {
        lock (Lock) _known = null;
    }

    private static Dictionary<string, string> ReadAll()
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM shop_settings;";

        using var reader = command.ExecuteReader();
        while (reader.Read()) all[reader.Str(0)] = reader.Str(1);
        return all;
    }

    /// <summary>
    /// Moves a machine's existing settings file into the database, once.
    ///
    /// A shop upgrading to this build has its name and its receipt footer in JSON, and nowhere
    /// else. Reading them across on the first run is the difference between an upgrade nobody
    /// notices and a shop whose receipts suddenly say "Market". Only values the database does
    /// not already have are taken, so the machine that is already the shop's server wins and a
    /// till plugged in later cannot overwrite the shop with its own defaults.
    /// </summary>
    public static void TakeOverFrom(IReadOnlyDictionary<string, string> fromTheFile)
    {
        try
        {
            lock (Lock) _known = ReadAll();

            foreach (var (key, value) in fromTheFile)
            {
                if (value.Length == 0) continue;
                if (Get(key, string.Empty).Length > 0) continue;

                Set(key, value);
            }
        }
        catch
        {
            // The file stays where it is; nothing is lost by not having moved it yet.
        }
    }

    // The keys themselves, named once so a typo cannot silently split a setting in two.
    public const string BusinessName = "business.name";
    public const string BusinessAddress = "business.address";
    public const string BusinessPhone = "business.phone";
    public const string TaxId = "business.tax_id";
    public const string Currency = "business.currency";
    public const string ReceiptFooter = "receipt.footer";
    public const string DefaultLowStock = "stock.default_low";
}
