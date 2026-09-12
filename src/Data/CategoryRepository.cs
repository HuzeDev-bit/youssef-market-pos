using Microsoft.Data.Sqlite;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>Category management. Categories are how a cashier finds a product that has no barcode.</summary>
public static class CategoryRepository
{
    public static List<CategoryRow> List(bool includeInactive = false)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT c.id, c.name, c.icon, c.is_active, c.image,
                   (SELECT COUNT(*) FROM products p WHERE p.category_id = c.id AND p.is_active = 1)
            FROM categories c
            WHERE TRIM(c.name) <> ''{(includeInactive ? string.Empty : " AND c.is_active = 1")}
            ORDER BY c.name;
            """;

        var rows = new List<CategoryRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CategoryRow
            {
                Id = reader.Int(0),
                Name = reader.Str(1),
                Icon = reader.Str(2),
                IsActive = reader.Bool(3),
                Image = reader.Str(4),
                ProductCount = reader.Int(5),
            });
        }
        return rows;
    }

    public static int Create(string name, string icon = "", string image = "")
    {
        Session.Require(Permission.ManageCategories);

        using var connection = Database.Open();

        // A name that belongs to a category the shop hid, back when hiding was what removing
        // did, is a name the shop cannot see and cannot use. Typing it again means it wants
        // that category, so this puts the old row back rather than refusing over a row no
        // screen would show.
        using var hidden = connection.CreateCommand();
        hidden.CommandText = "SELECT id FROM categories WHERE name = $name AND is_active = 0;";
        hidden.With("$name", name.Trim());
        if (hidden.ExecuteScalar() is { } found and not DBNull)
        {
            var back = Convert.ToInt32(found);

            using var revive = connection.CreateCommand();
            revive.CommandText =
                "UPDATE categories SET is_active = 1, icon = $icon, image = $image WHERE id = $id;";
            revive.With("$icon", icon).With("$image", image).With("$id", back).ExecuteNonQuery();

            ActivityRepository.Record("added category", "Category", back, newValue: name,
                                      detail: ActivityRepository.Say("added category {0}", name));
            return back;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO categories (name, icon, is_active, image) VALUES ($name, $icon, 1, $image);
            SELECT last_insert_rowid();
            """;
        command.With("$name", name.Trim()).With("$icon", icon).With("$image", image);
        var id = Convert.ToInt32(command.ExecuteScalar());

        ActivityRepository.Record("added category", "Category", id, newValue: name,
                                  detail: ActivityRepository.Say("added category {0}", name));
        return id;
    }

    public static void Rename(int id, string oldName, string newName, string icon, string image)
    {
        Session.Require(Permission.ManageCategories);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE categories SET name = $name, icon = $icon, image = $image WHERE id = $id;";
        command.With("$name", newName.Trim()).With("$icon", icon).With("$image", image).With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record("renamed category", "Category", id,
            oldValue: oldName, newValue: newName, detail: ActivityRepository.Say("renamed a category"));
    }

    /// <summary>
    /// Deletes a category outright, whatever is still in it.
    ///
    /// <para>
    /// Hiding it was not what the shop meant. A category made by mistake, or one the shop has
    /// stopped stocking, was left in the table for ever holding on to its name — so typing the
    /// name again came back "there is already a category called that", about something no
    /// screen in the app would show. What is deleted has to be gone.
    /// </para>
    ///
    /// <para>
    /// Its products are not. They stay in stock and on the till, sold by their barcode, and
    /// simply stop belonging to a category. They used to be moved into Other instead, which
    /// could not work when Other was the category being deleted: making a new Other ran into
    /// the old one's name, and the delete came back as a database error.
    /// </para>
    /// </summary>
    public static bool Delete(int id, string name, out string problem)
    {
        Session.Require(Permission.ManageCategories);
        problem = string.Empty;

        using var connection = Database.Open();
        using var work = connection.BeginTransaction();
        try
        {
            using var move = connection.CreateCommand();
            move.Transaction = work;
            move.CommandText = "UPDATE products SET category_id = $none WHERE category_id = $id;";
            move.With("$id", id).With("$none", NoCategory(connection, work)).ExecuteNonQuery();

            using var drop = connection.CreateCommand();
            drop.Transaction = work;
            drop.CommandText = "DELETE FROM categories WHERE id = $id;";
            drop.With("$id", id).ExecuteNonQuery();

            work.Commit();
        }
        catch (SqliteException held)
        {
            // Something else is still holding on. The database says so by refusing, and
            // saying which is more use than saying "cannot".
            work.Rollback();
            problem = Loc.T("{0} could not be deleted: something in the shop's records still "
                          + "points at it. ({1})", name, held.Message);
            return false;
        }

        ActivityRepository.Record("deleted category", "Category", id, oldValue: name,
            detail: ActivityRepository.Say("deleted category {0}", name));

        CategoryImages.Forget(id);
        return true;
    }

    /// <summary>
    /// Where a product sits once its category has been deleted.
    ///
    /// A product's category column cannot be empty, so "no category" is a row of its own. It
    /// has no name, which is what keeps it off every list of categories in the app — the
    /// Categories page, the till's chips and the product forms all pass over a blank name.
    /// </summary>
    private static int NoCategory(SqliteConnection connection, SqliteTransaction work)
    {
        using var command = connection.CreateCommand();
        command.Transaction = work;
        command.CommandText = """
            INSERT INTO categories (name, is_active) VALUES ('', 0) ON CONFLICT(name) DO NOTHING;
            SELECT id FROM categories WHERE name = '';
            """;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static bool SetActive(int id, string name, bool active, out string problem)
    {
        Session.Require(Permission.ManageCategories);
        problem = string.Empty;

        using var connection = Database.Open();

        if (!active)
        {
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM products WHERE category_id = $id AND is_active = 1;";
            check.With("$id", id);
            var count = Convert.ToInt32(check.ExecuteScalar());
            if (count > 0)
            {
                // Said here rather than at the screen: two different pages show this refusal,
                // and a sentence built out of an English plural would have reached both of
                // them untranslated.
                problem = Loc.T(count == 1
                    ? "{0} still has {1} product in it. Move it to another category first."
                    : "{0} still has {1} products in it. Move them to another category first.",
                    name, count);
                return false;
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE categories SET is_active = $active WHERE id = $id;";
        command.With("$active", active ? 1 : 0).With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record(active ? "reactivated category" : "deactivated category",
            "Category", id, newValue: name, detail: ActivityRepository.Say(active ? "reactivated category {0}"
                                                : "deactivated category {0}", name));
        return true;
    }
}
