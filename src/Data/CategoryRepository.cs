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
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO categories (name, icon, is_active, image) VALUES ($name, $icon, 1, $image);
            SELECT last_insert_rowid();
            """;
        command.With("$name", name.Trim()).With("$icon", icon).With("$image", image);
        var id = Convert.ToInt32(command.ExecuteScalar());

        ActivityRepository.Record("added category", "Category", id, newValue: name,
                                  detail: $"added category {name}");
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
            oldValue: oldName, newValue: newName, detail: "renamed a category");
    }

    /// <summary>
    /// Deactivates rather than deletes, because products point at this row. A category with
    /// products still assigned is refused outright — silently orphaning stock is worse than
    /// an error message.
    /// </summary>
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
                problem = $"{name} still has {count} active product{(count == 1 ? string.Empty : "s")}. "
                        + "Move them to another category first.";
                return false;
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE categories SET is_active = $active WHERE id = $id;";
        command.With("$active", active ? 1 : 0).With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record(active ? "reactivated category" : "deactivated category",
            "Category", id, newValue: name, detail: $"{(active ? "reactivated" : "deactivated")} category {name}");
        return true;
    }
}
