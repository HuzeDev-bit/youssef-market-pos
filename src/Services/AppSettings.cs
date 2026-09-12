using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketPos.Services;

/// <summary>
/// Till settings, stored as JSON next to the database so they survive reinstalls.
/// Small and hand-editable on purpose — a shop owner on the phone can be talked through
/// fixing a printer name in Notepad.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Windows printer name. Empty means "use the Windows default printer".</summary>
    public string ReceiptPrinterName { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash and salt of the admin password. Never the password itself.</summary>
    /// <summary>
    /// What the owner is called. Typed at the lock the first time they sign in, and used
    /// from then on wherever their name is recorded — an owner is not a worker, so there is
    /// no staff row to read it from.
    /// </summary>
    public string OwnerName { get; set; } = string.Empty;

    public string AdminPasswordHash { get; set; } = string.Empty;
    public string AdminPasswordSalt { get; set; } = string.Empty;

    /// <summary>
    /// Whether this install has already been given the starting password. Written once, and
    /// read for ever after, so a shop that deliberately turns the password off does not find
    /// it back the next morning.
    /// </summary>
    public bool AdminPasswordStarted { get; set; }

    // ---------------------------- Business details ----------------------------
    // Printed on receipts and shown in the back office.

    // These seven are the shop's, not this computer's, so they live in the database with the
    // rest of the shop - see ShopSettings. They are still reached through here, because every
    // screen already asks this class for them and none of them should have to know where a
    // setting sleeps. JsonIgnore keeps them out of the machine's own file, which is what
    // stopped two tills disagreeing about the shop's name.

    [JsonIgnore]
    public string BusinessName
    {
        get => ShopSettings.Get(ShopSettings.BusinessName, "Market");
        set => ShopSettings.Set(ShopSettings.BusinessName, value);
    }

    [JsonIgnore]
    public string BusinessAddress
    {
        get => ShopSettings.Get(ShopSettings.BusinessAddress, string.Empty);
        set => ShopSettings.Set(ShopSettings.BusinessAddress, value);
    }

    [JsonIgnore]
    public string BusinessPhone
    {
        get => ShopSettings.Get(ShopSettings.BusinessPhone, string.Empty);
        set => ShopSettings.Set(ShopSettings.BusinessPhone, value);
    }

    /// <summary>Moroccan tax id (ICE / IF), printed on the receipt when filled in.</summary>
    [JsonIgnore]
    public string TaxId
    {
        get => ShopSettings.Get(ShopSettings.TaxId, string.Empty);
        set => ShopSettings.Set(ShopSettings.TaxId, value);
    }

    /// <summary>Currency suffix. MAD is the default and the only one the shop trades in.</summary>
    [JsonIgnore]
    public string Currency
    {
        get => ShopSettings.Get(ShopSettings.Currency, "DH");
        set => ShopSettings.Set(ShopSettings.Currency, value);
    }

    /// <summary>Line printed under the total, e.g. "Choukran — thank you".</summary>
    [JsonIgnore]
    public string ReceiptFooter
    {
        get => ShopSettings.Get(ShopSettings.ReceiptFooter, "Choukran / Merci");
        set => ShopSettings.Set(ShopSettings.ReceiptFooter, value);
    }

    /// <summary>
    /// Fallback minimum stock for products that have not been given their own. A shop that
    /// never fills this in still gets low-stock warnings instead of silence.
    /// </summary>
    [JsonIgnore]
    public decimal DefaultLowStock
    {
        get => ShopSettings.GetNumber(ShopSettings.DefaultLowStock, 5m);
        set => ShopSettings.SetNumber(ShopSettings.DefaultLowStock, value);
    }

    /// <summary>Where exports and backups are written. Empty means the user's Documents folder.</summary>
    public string ExportFolder { get; set; } = string.Empty;

    /// <summary>
    /// Whether the app carries its own on-screen keyboard.
    ///
    /// Null is not "off" — it is "nobody has said", which is what every shop has until it
    /// opens Settings, and most never will. The app then decides from the machine: a keyboard
    /// on a touchscreen till, and nothing at all on a counter with a real one plugged into it.
    /// Ticking or clearing the box in Settings writes a true or a false and settles it.
    /// </summary>
    public bool? OnScreenKeyboard { get; set; }

    /// <summary>
    /// How big the shop has made the on-screen keyboard, as a share of its natural size.
    /// Null until somebody presses − or + on it. Kept here rather than in the window so a size
    /// chosen once with a fingertip is not chosen again every morning.
    /// </summary>
    public double? KeyboardScale { get; set; }

    /// <summary>
    /// Which language the interface is in: "en", "fr" or "ar".
    ///
    /// Arabic by default, because this is one shop's software and that is the language spoken
    /// in it. A machine with no settings file yet — every machine, the first time it is
    /// switched on — opens in the shop's own language rather than in the one the code happens
    /// to be written in.
    ///
    /// Stored as a code rather than an enum so the file stays readable to somebody being
    /// talked through it on the phone, which is the whole reason these settings are JSON.
    /// </summary>
    public string Language { get; set; } = "ar";

    // ---------------------------- The shop's network ----------------------------

    /// <summary>
    /// Where the back-office machine answers, e.g. "http://192.168.1.20:5000".
    ///
    /// Empty is the ordinary case and means this machine works alone: one computer, its own
    /// database, no network at all. A shop only fills this in when it puts a second till on
    /// the counter, and from then on this machine keeps its own copy of the catalogue and
    /// hands its sales over to the machine that owns the books.
    /// </summary>
    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>
    /// What this machine calls itself when it hands a sale over. Every sale carries it, so two
    /// tills can never mint the same reference and have the server take one for a repeat of
    /// the other. Defaults to the computer's own name.
    /// </summary>
    public string TillName { get; set; } = string.Empty;

    /// <summary>
    /// The fingerprint of the shop's server certificate, written down the first time this
    /// machine reached it.
    ///
    /// Device configuration, not business data: it describes which machine this one is paired
    /// with. Clearing it re-pairs the till, which is what a shop does after rebuilding its
    /// server — and what nobody should be doing otherwise, because a key that changed on its
    /// own is the one thing this is here to notice.
    /// </summary>
    public string ServerFingerprint { get; set; } = string.Empty;

    /// <summary>The till's name, falling back to the machine's — never empty in practice.</summary>
    [JsonIgnore]
    public string TillLabel =>
        string.IsNullOrWhiteSpace(TillName) ? Environment.MachineName : TillName.Trim();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MarketPos", "settings.json");

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch
        {
            // A corrupt settings file must never stop the till opening.
        }
        return new AppSettings();
    }

    /// <summary>
    /// Moves the shop-wide settings out of this machine's file and into the database, once.
    ///
    /// Called at start-up, after the database is open. A shop upgrading to this build has its
    /// name and its receipt footer in JSON and nowhere else; reading them across is the
    /// difference between an upgrade nobody notices and a shop whose receipts suddenly say
    /// "Market". The file is left exactly as it is — nothing is lost if this has to be run
    /// again — and only settings the database does not already have are taken, so a till
    /// plugged in later cannot overwrite the shop with its own defaults.
    /// </summary>
    public static void MoveShopSettingsIntoTheDatabase()
    {
        try
        {
            if (!File.Exists(Path)) return;

            using var file = JsonDocument.Parse(File.ReadAllText(Path));
            var was = file.RootElement;

            var moving = new Dictionary<string, string>(StringComparer.Ordinal);

            void Take(string jsonName, string key)
            {
                if (!was.TryGetProperty(jsonName, out var value)) return;

                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.Number => value.GetDecimal()
                        .ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => string.Empty,
                };

                if (text.Length > 0) moving[key] = text;
            }

            Take(nameof(BusinessName), ShopSettings.BusinessName);
            Take(nameof(BusinessAddress), ShopSettings.BusinessAddress);
            Take(nameof(BusinessPhone), ShopSettings.BusinessPhone);
            Take(nameof(TaxId), ShopSettings.TaxId);
            Take(nameof(Currency), ShopSettings.Currency);
            Take(nameof(ReceiptFooter), ShopSettings.ReceiptFooter);
            Take(nameof(DefaultLowStock), ShopSettings.DefaultLowStock);

            if (moving.Count > 0) ShopSettings.TakeOverFrom(moving);
        }
        catch
        {
            // An unreadable file is not a reason to refuse to open the till.
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Losing a setting is survivable; crashing mid-shift is not.
        }
    }
}
