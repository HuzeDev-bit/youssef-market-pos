using MarketPos.Link;
using MarketPos.Models;

namespace MarketPos.Data;

/// <summary>
/// Writes the server's catalogue into this machine's own database.
///
/// A till keeps a full copy rather than asking the server for each scan. A shop's network is a
/// cable behind a fridge; a till that needs an answer from another computer before it can put
/// bread in the basket is a till that stops working on the day it is busiest.
///
/// The copy keeps the server's row ids. That matters: a sale handed over later names the
/// product it sold, and an id that meant something different on each machine would take stock
/// off the wrong shelf. The barcode is the better key and is what the server matches on, but
/// keeping ids aligned as well means the two databases can be read side by side.
///
/// What does not travel: cost, supplier, minimum stock, photographs. A till has no business
/// knowing what the shop paid, and none of it is needed to sell anything.
/// </summary>
public static class CatalogSync
{
    /// <summary>
    /// Replaces the local catalogue with what the server sent. Products the server no longer
    /// has are deactivated rather than deleted — sales already taken on this till point at
    /// them, and a deleted row would orphan a line of somebody's receipt.
    /// </summary>
    public static int Apply(IReadOnlyList<CatalogItem> items)
    {
        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        var categories = CategoryIds(connection, items.Select(i => i.Category));

        foreach (var item in items)
        {
            // A machine that used to work alone has products of its own, and one of them may
            // be holding the barcode this one arrives with. The server's copy is the truth, so
            // the local stray steps aside — deactivated, not deleted, because a sale already
            // taken on this till may point at it.
            if (item.Barcode.Length > 0)
            {
                using var displace = connection.CreateCommand();
                displace.CommandText = """
                    UPDATE products SET barcode = 'replaced-' || id, is_active = 0, show_in_pos = 0
                    WHERE barcode = $barcode AND id <> $id;
                    """;
                displace.WithBarcode("$barcode", item.Barcode).With("$id", item.Id);
                displace.ExecuteNonQuery();
            }

            using var upsert = connection.CreateCommand();
            upsert.CommandText = """
                INSERT INTO products(id, barcode, name, category_id, price, unit, tax_rate,
                                     stock, is_active, show_in_pos, created_at, updated_at)
                VALUES($id, $barcode, $name, $category, $price, $unit, $tax, $stock, 1, 1, $now, $now)
                ON CONFLICT(id) DO UPDATE SET
                    barcode     = excluded.barcode,
                    name        = excluded.name,
                    category_id = excluded.category_id,
                    price       = excluded.price,
                    unit        = excluded.unit,
                    tax_rate    = excluded.tax_rate,
                    stock       = excluded.stock,
                    is_active   = 1,
                    show_in_pos = 1,
                    updated_at  = excluded.updated_at;
                """;
            upsert.With("$id", item.Id)
                  .WithBarcode("$barcode", item.Barcode)
                  .With("$name", item.Name)
                  .With("$category", categories[Grouping(item.Category)])
                  .WithMoney("$price", item.Price)
                  .With("$unit", item.Unit)
                  .WithMoney("$tax", item.TaxRate)
                  .WithMoney("$stock", item.Stock)
                  .WithDate("$now", DateTime.Now);
            upsert.ExecuteNonQuery();
        }

        // Anything the server did not mention is no longer for sale here.
        using var retire = connection.CreateCommand();
        var ids = items.Select(i => i.Id.ToString()).ToList();
        retire.CommandText = ids.Count == 0
            ? "UPDATE products SET show_in_pos = 0, is_active = 0;"
            : $"UPDATE products SET show_in_pos = 0, is_active = 0 WHERE id NOT IN ({string.Join(",", ids)});";
        retire.ExecuteNonQuery();

        transaction.Commit();
        return items.Count;
    }

    /// <summary>
    /// The category rows the incoming products need, created where they are missing.
    /// products.category_id is NOT NULL, so a product whose category has not arrived yet would
    /// otherwise fail to insert and take the whole catalogue down with it.
    /// </summary>
    private static Dictionary<string, int> CategoryIds(
        Microsoft.Data.Sqlite.SqliteConnection connection, IEnumerable<string> names)
    {
        var wanted = names.Select(Grouping).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in wanted)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO categories(name, is_active) VALUES($name, 1)
                ON CONFLICT(name) DO UPDATE SET is_active = 1;
                SELECT id FROM categories WHERE name = $name;
                """;
            command.With("$name", name);
            ids[name] = Convert.ToInt32(command.ExecuteScalar());
        }

        return ids;
    }

    /// <summary>A product with no category still has to land somewhere.</summary>
    private static string Grouping(string category) =>
        string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();

    /// <summary>
    /// What the till last saw, so it can ask the server only for what has changed since.
    /// Kept beside the catalogue rather than in settings: it describes this database, and a
    /// settings file that outlived a restored backup would claim the till was up to date.
    /// </summary>
    public static string Stamp
    {
        get => Meta.Get("catalog_stamp");
        set => Meta.Set("catalog_stamp", value);
    }

    /// <summary>Whether this database has ever been handed a catalogue by a server.</summary>
    public static bool HasEverSynced => Stamp.Length > 0;

    /// <summary>
    /// Takes everything off this till's shelves, because none of it is this till's to sell.
    ///
    /// A till is a copy of a shop. A machine that has never been handed that copy has no
    /// business showing products at all — but if the app was ever run on it as a shop of its
    /// own, or a database was carried over from somewhere, it has rows, and it will happily
    /// put them on screen and sell them. Somebody opens the till on their laptop and sees a
    /// shop: the wrong products, the wrong prices, and sales that no book in the world will
    /// ever record.
    ///
    /// Retired rather than deleted, and the same way the server retires them, because a sale
    /// already taken on this machine may point at one of them.
    /// </summary>
    public static int ForgetEverything()
    {
        using var connection = Database.Open();

        int cleared;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE products SET show_in_pos = 0, is_active = 0 WHERE is_active = 1;";
            cleared = command.ExecuteNonQuery();
        }

        // And the note saying what this till last saw. Without this the server would compare
        // the till's stamp to its own, decide nothing had changed, send nothing — and the
        // shelves just emptied would stay empty while the chip said connected.
        Stamp = string.Empty;
        return cleared;
    }
}
