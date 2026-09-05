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

    /// <summary>Print the receipt automatically the moment a sale completes.</summary>
    public bool AutoPrintReceipts { get; set; } = true;

    /// <summary>PBKDF2 hash and salt of the admin password. Never the password itself.</summary>
    /// <summary>
    /// What the owner is called. Typed at the lock the first time they sign in, and used
    /// from then on wherever their name is recorded — an owner is not a worker, so there is
    /// no staff row to read it from.
    /// </summary>
    public string OwnerName { get; set; } = string.Empty;

    public string AdminPasswordHash { get; set; } = string.Empty;
    public string AdminPasswordSalt { get; set; } = string.Empty;

    // ---------------------------- Business details ----------------------------
    // Printed on receipts and shown in the back office.

    public string BusinessName { get; set; } = "Market";
    public string BusinessAddress { get; set; } = string.Empty;
    public string BusinessPhone { get; set; } = string.Empty;

    /// <summary>Moroccan tax id (ICE / IF), printed on the receipt when filled in.</summary>
    public string TaxId { get; set; } = string.Empty;

    /// <summary>Currency suffix. MAD is the default and the only one the shop trades in.</summary>
    public string Currency { get; set; } = "DH";

    /// <summary>Line printed under the total, e.g. "Choukran — thank you".</summary>
    public string ReceiptFooter { get; set; } = "Choukran / Merci";

    /// <summary>
    /// Fallback minimum stock for products that have not been given their own. A shop that
    /// never fills this in still gets low-stock warnings instead of silence.
    /// </summary>
    public decimal DefaultLowStock { get; set; } = 5m;

    /// <summary>Where exports and backups are written. Empty means the user's Documents folder.</summary>
    public string ExportFolder { get; set; } = string.Empty;

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
