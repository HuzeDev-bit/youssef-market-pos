using System.Globalization;
using MarketPos.Models;
using MarketPos.Services;
using Microsoft.Data.Sqlite;

namespace MarketPos.Data;

/// <summary>Reads and writes the product catalogue.</summary>
public static class ProductRepository
{
    public static List<Product> GetAll()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.barcode, p.name, c.name, p.price, p.unit, p.tax_rate, p.image_path,
                   p.show_in_pos
            FROM products p
            JOIN categories c ON c.id = p.category_id
            WHERE p.is_active = 1
            ORDER BY p.name;
            """;

        var products = new List<Product>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            products.Add(new Product
            {
                Id = reader.GetInt32(0),
                Barcode = reader.GetString(1),
                Name = reader.GetString(2),
                Category = reader.GetString(3),
                Price = ParseMoney(reader.GetString(4)),
                Unit = reader.GetString(5) == nameof(Unit.Kg) ? Unit.Kg : Unit.Each,
                TaxRate = ParseMoney(reader.GetString(6)),
                // An explicit path in the database wins; otherwise look for a file named
                // after the barcode, so photos can be added without touching data or code.
                ImagePath = reader.IsDBNull(7)
                    ? ProductImages.Find(reader.GetString(1))
                    : reader.GetString(7),
                SoldAtTheTill = reader.IsDBNull(8) || reader.GetInt32(8) != 0,
            });
        }
        return products;
    }

    public static List<string> GetCategories()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM categories ORDER BY name;";

        var categories = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) categories.Add(reader.GetString(0));
        return categories;
    }



    internal static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    internal static decimal ParseMoney(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
