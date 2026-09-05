using System.IO;
using Microsoft.Data.Sqlite;

namespace MarketPos.Data;

/// <summary>
/// Owns the single SQLite file and its schema. No server, no config — the database lives
/// in %AppData%\MarketPos so it survives reinstalls and needs no admin rights to write.
/// </summary>
public static class Database
{
    private static string? _path;

    public static string Path => _path ??= BuildPath();

    private static string BuildPath()
    {
        // An override exists only so the self-test can run the whole money flow against a
        // scratch file. A shop machine never sets it, and the till has no UI for it — a
        // second database is not something a cashier should be able to end up in.
        var overridePath = Environment.GetEnvironmentVariable("MARKETPOS_DB");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(overridePath)!);
            return overridePath;
        }

        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarketPos");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "marketpos.db");
    }

    public static SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={Path}");
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    /// <summary>Creates the schema on first run. Safe to call on every startup.</summary>
    public static void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        // Money is stored as TEXT, not REAL. SQLite's REAL is a double, and doubles cannot
        // represent 0.10 exactly — totals would drift by centimes over thousands of sales.
        // Invariant-culture strings round-trip a decimal exactly.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS categories (
                id    INTEGER PRIMARY KEY AUTOINCREMENT,
                name  TEXT    NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS products (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                barcode     TEXT    NOT NULL UNIQUE,
                name        TEXT    NOT NULL,
                category_id INTEGER NOT NULL REFERENCES categories(id),
                price       TEXT    NOT NULL,
                unit        TEXT    NOT NULL,
                tax_rate    TEXT    NOT NULL,
                emoji       TEXT    NOT NULL DEFAULT '',
                image_path  TEXT,
                is_active   INTEGER NOT NULL DEFAULT 1,
                created_at  TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_products_barcode ON products(barcode);
            CREATE INDEX IF NOT EXISTS ix_products_name    ON products(name);

            CREATE TABLE IF NOT EXISTS sales (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                invoice_number  INTEGER NOT NULL UNIQUE,
                sold_at         TEXT    NOT NULL,
                subtotal        TEXT    NOT NULL,
                tax             TEXT    NOT NULL,
                total           TEXT    NOT NULL,
                payment_method  TEXT    NOT NULL,
                amount_tendered TEXT,
                change_given    TEXT,
                is_voided       INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_sales_sold_at ON sales(sold_at);

            -- Lines keep their own copy of name/price on purpose. A product can be renamed
            -- or repriced later; a receipt reprinted next year must still show what was
            -- actually charged on the day.
            CREATE TABLE IF NOT EXISTS sale_lines (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                sale_id    INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
                product_id INTEGER          REFERENCES products(id),
                barcode    TEXT    NOT NULL,
                name       TEXT    NOT NULL,
                unit       TEXT    NOT NULL,
                unit_price TEXT    NOT NULL,
                quantity   TEXT    NOT NULL,
                tax_rate   TEXT    NOT NULL,
                line_total TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_sale_lines_sale ON sale_lines(sale_id);
            """;
        command.ExecuteNonQuery();

        AddDiscountColumns(connection);
        Schema.Apply(connection);
    }

    /// <summary>
    /// Adds the remise columns to databases created before the feature existed. SQLite has no
    /// "ADD COLUMN IF NOT EXISTS", so existing columns are detected first — this keeps a till
    /// that already has sales history working after an update.
    /// </summary>
    private static void AddDiscountColumns(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var columns = connection.CreateCommand())
        {
            columns.CommandText = "PRAGMA table_info(sales);";
            using var reader = columns.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(1));
        }

        foreach (var (name, ddl) in new[]
                 {
                     ("gross_before_discount", "ALTER TABLE sales ADD COLUMN gross_before_discount TEXT NOT NULL DEFAULT '0';"),
                     ("discount_kind",         "ALTER TABLE sales ADD COLUMN discount_kind TEXT NOT NULL DEFAULT 'None';"),
                     ("discount_value",        "ALTER TABLE sales ADD COLUMN discount_value TEXT NOT NULL DEFAULT '0';"),
                     ("discount_amount",       "ALTER TABLE sales ADD COLUMN discount_amount TEXT NOT NULL DEFAULT '0';"),
                 })
        {
            if (existing.Contains(name)) continue;
            using var alter = connection.CreateCommand();
            alter.CommandText = ddl;
            alter.ExecuteNonQuery();
        }
    }
}
