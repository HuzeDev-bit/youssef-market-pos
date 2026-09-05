using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>
/// The back-office view of the catalogue: cost, stock, supplier, shelf and expiry as well as
/// the shelf price the till already knew about.
///
/// Stock is read here but never written here — <see cref="InventoryRepository.Move"/> owns
/// that column so no change can happen without a reason attached to it.
/// </summary>
public static class StockRepository
{
    private const string Select = """
        SELECT p.id, p.barcode, p.name, p.sku, c.name, p.category_id, p.cost, p.price,
               p.stock, p.min_stock, p.unit, p.tax_rate, p.shelf, p.supplier_id,
               COALESCE(s.name, ''), p.expires_on, p.show_in_pos, p.is_active,
               p.image_path, p.created_at
        FROM products p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN suppliers s ON s.id = p.supplier_id
        """;

    public static List<StockItem> List(string? search = null, int? categoryId = null,
                                       StockStatus? status = null, bool includeInactive = false)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (!includeInactive) where.Add("p.is_active = 1");
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(p.name LIKE $q OR p.barcode LIKE $q OR p.sku LIKE $q)");
            command.With("$q", $"%{search.Trim()}%");
        }
        if (categoryId is { } cid)
        {
            where.Add("p.category_id = $cid");
            command.With("$cid", cid);
        }

        command.CommandText = $"""
            {Select}
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY p.name;
            """;

        var items = Read(command);

        // Status is a derived property (stock vs min_stock), so it is filtered in memory
        // rather than duplicated as a second definition in SQL.
        return status is { } s ? items.Where(i => i.Status == s).ToList() : items;
    }

    public static StockItem? Find(int id)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"{Select} WHERE p.id = $id;";
        command.With("$id", id);
        return Read(command).FirstOrDefault();
    }

    /// <summary>
    /// The product a scanner just read. Exact match, and inactive products included: a barcode
    /// that has been withdrawn from sale still has to answer "what is this", or the cashier
    /// holding it is told the shop has never heard of a thing it sold last week.
    /// </summary>
    public static StockItem? FindByBarcode(string barcode)
    {
        barcode = barcode.Trim();
        if (barcode.Length == 0) return null;

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"{Select} WHERE p.barcode = $code ORDER BY p.is_active DESC;";
        command.With("$code", barcode);
        return Read(command).FirstOrDefault();
    }

    private static List<StockItem> Read(Microsoft.Data.Sqlite.SqliteCommand command)
    {
        var items = new List<StockItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var barcode = reader.Str(1);
            items.Add(new StockItem
            {
                Id = reader.Int(0),
                Barcode = barcode,
                Name = reader.Str(2),
                Sku = reader.Str(3),
                Category = reader.Str(4),
                CategoryId = reader.Int(5),
                Cost = reader.Dec(6),
                Price = reader.Dec(7),
                Stock = reader.Dec(8),
                MinStock = reader.Dec(9),
                Unit = reader.Str(10) == nameof(Unit.Kg) ? Unit.Kg : Unit.Each,
                TaxRate = reader.Dec(11),
                Shelf = reader.Str(12),
                SupplierId = reader.IsDBNull(13) ? null : reader.Int(13),
                SupplierName = reader.Str(14),
                ExpiresOn = reader.DateOrNull(15),
                ShowInPos = reader.Bool(16),
                IsActive = reader.Bool(17),
                ImagePath = reader.IsDBNull(18) ? ProductImages.Find(barcode) : reader.Str(18),
                CreatedAt = reader.Date(19),
            });
        }
        return items;
    }

    /// <summary>
    /// The catalogue newest first — what the till's Add product page lists, so the cashier can
    /// see the work they have just done and go back into any of it.
    /// </summary>
    public static List<StockItem> RecentlyAdded(int limit = 60)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            {Select}
            WHERE p.is_active = 1
            ORDER BY p.created_at DESC, p.id DESC
            LIMIT $limit;
            """;
        command.With("$limit", limit);
        return Read(command);
    }

    /// <summary>
    /// Creates a product. Opening stock goes in as a movement rather than a direct write, so
    /// even the first quantity a product ever had has a reason and a date against it.
    /// </summary>
    public static int Create(StockItem item, decimal openingStock = 0m)
    {
        // Either side may create: the back office managing the catalogue, or a cashier adding
        // the thing in their hand so the customer can pay for it.
        Session.RequireAny(Permission.ManageProducts, Permission.AddProductAtTill);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        using (var category = connection.CreateCommand())
        {
            category.CommandText = "INSERT OR IGNORE INTO categories (name) VALUES ($name);";
            category.With("$name", Grouping(item));
            category.ExecuteNonQuery();
        }

        int id;
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO products
                    (barcode, name, sku, category_id, cost, price, stock, min_stock, unit,
                     tax_rate, shelf, supplier_id, expires_on, show_in_pos, image_path,
                     is_active, created_at, updated_at)
                VALUES
                    ($barcode, $name, $sku, (SELECT id FROM categories WHERE name = $category),
                     $cost, $price, '0', $minStock, $unit, $taxRate, $shelf, $supplierId,
                     $expires, $showInPos, $imagePath, 1, $now, $now);
                SELECT last_insert_rowid();
                """;
            Bind(insert, item);
            insert.WithDate("$now", DateTime.Now);
            id = Convert.ToInt32(insert.ExecuteScalar());
        }

        if (openingStock != 0m)
            InventoryRepository.Move(id, openingStock, StockReason.OpeningStock,
                                     reference: "New product", unitCost: item.Cost, connection: connection);

        ActivityRepository.Record("added product", "Product", id, newValue: item.Name,
                                  detail: $"added product {item.Name}", connection: connection);

        transaction.Commit();
        return id;
    }

    /// <summary>
    /// Takes delivery of goods at the till: adds the quantity to stock and, where the cashier
    /// entered them, refreshes the buying price, the selling price and the expiry date.
    ///
    /// Separate from <see cref="Update"/> so a cashier can put arriving goods into the shop
    /// without being handed the whole catalogue — this touches one product's prices and its
    /// stock, and only in the direction of goods coming in.
    /// </summary>
    public static void ReceiveAtTill(int productId, decimal quantity,
                                     decimal? cost = null, decimal? price = null,
                                     DateTime? expiresOn = null)
    {
        Session.RequireAny(Permission.ManageProducts, Permission.AddProductAtTill);

        var before = Find(productId) ?? throw new InvalidOperationException("That product no longer exists.");

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        if (cost is not null || price is not null || expiresOn is not null)
        {
            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE products SET
                    cost = COALESCE($cost, cost),
                    price = COALESCE($price, price),
                    expires_on = COALESCE($expires, expires_on),
                    updated_at = $now
                WHERE id = $id;
                """;
            update.With("$cost", cost is null ? null : Db.Money(cost.Value))
                  .With("$price", price is null ? null : Db.Money(price.Value))
                  .WithDate("$expires", expiresOn)
                  .WithDate("$now", DateTime.Now)
                  .With("$id", productId);
            update.ExecuteNonQuery();
        }

        if (quantity > 0m)
            InventoryRepository.Move(productId, quantity, StockReason.SupplierPurchase,
                                     reference: "Received at till", unitCost: cost ?? before.Cost,
                                     connection: connection);

        if (price is not null && price != before.Price)
            ActivityRepository.Record("changed selling price", "Product", productId,
                oldValue: $"{before.Price:0.00} DH", newValue: $"{price:0.00} DH",
                detail: $"changed {before.Name} selling price", connection: connection);

        ActivityRepository.Record("received stock at the till", "Product", productId,
            oldValue: before.Stock.ToString("0.###"),
            newValue: (before.Stock + quantity).ToString("0.###"),
            detail: $"received {quantity:0.###} of {before.Name}", connection: connection);

        transaction.Commit();
    }

    /// <summary>
    /// Updates the editable fields. Stock is not among them on purpose: use the Inventory
    /// page, which records why the number moved.
    /// </summary>
    public static void Update(StockItem item)
    {
        Session.Require(Permission.ManageProducts);

        var before = Find(item.Id);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        using (var category = connection.CreateCommand())
        {
            category.CommandText = "INSERT OR IGNORE INTO categories (name) VALUES ($name);";
            category.With("$name", Grouping(item));
            category.ExecuteNonQuery();
        }

        using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE products SET
                    barcode = $barcode, name = $name, sku = $sku,
                    category_id = (SELECT id FROM categories WHERE name = $category),
                    cost = $cost, price = $price, min_stock = $minStock, unit = $unit,
                    tax_rate = $taxRate, shelf = $shelf, supplier_id = $supplierId,
                    expires_on = $expires, show_in_pos = $showInPos,
                    image_path = $imagePath, updated_at = $now
                WHERE id = $id;
                """;
            Bind(update, item);
            update.With("$id", item.Id).WithDate("$now", DateTime.Now);
            update.ExecuteNonQuery();
        }

        // A price change is the one edit worth spelling out in the log by name, because it
        // is the one that changes what a customer is charged.
        if (before is not null && before.Price != item.Price)
            ActivityRepository.Record("changed selling price", "Product", item.Id,
                oldValue: $"{before.Price:0.00} DH", newValue: $"{item.Price:0.00} DH",
                detail: $"changed {item.Name} selling price", connection: connection);
        else if (before is not null && before.Cost != item.Cost)
            ActivityRepository.Record("changed purchase price", "Product", item.Id,
                oldValue: $"{before.Cost:0.00} DH", newValue: $"{item.Cost:0.00} DH",
                detail: $"changed {item.Name} purchase price", connection: connection);
        else
            ActivityRepository.Record("edited product", "Product", item.Id, newValue: item.Name,
                detail: $"edited product {item.Name}", connection: connection);

        transaction.Commit();
    }

    /// <summary>
    /// The category to file this product under.
    ///
    /// The schema insists on one, so "no category" has to be a real shelf rather than a
    /// missing link. It used to be an empty name, which made a category called "" — a
    /// nameless card in the back office and a blank shelf on the till, with nothing looking
    /// wrong until somebody went looking. Anything unfiled goes to Other, which a shopkeeper
    /// can find and move things out of.
    /// </summary>
    private const string Unfiled = "Other";

    private static string Grouping(StockItem item) =>
        string.IsNullOrWhiteSpace(item.Category) ? Unfiled : item.Category.Trim();

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, StockItem item)
    {
        command.With("$barcode", item.Barcode)
               .With("$name", item.Name)
               .With("$sku", item.Sku)
               .With("$category", Grouping(item))
               .WithMoney("$cost", item.Cost)
               .WithMoney("$price", item.Price)
               .WithMoney("$minStock", item.MinStock)
               .With("$unit", item.Unit.ToString())
               .WithMoney("$taxRate", item.TaxRate)
               .With("$shelf", item.Shelf)
               .With("$supplierId", item.SupplierId)
               .WithDate("$expires", item.ExpiresOn)
               .With("$showInPos", item.ShowInPos ? 1 : 0)
               .With("$imagePath", item.ImagePath);
    }

    /// <summary>
    /// Deactivates a product. Never a DELETE: old sale lines point at this row, and a
    /// receipt from last year has to keep resolving.
    /// </summary>
    public static void SetActive(int id, string name, bool active)
    {
        Session.Require(Permission.ManageProducts);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE products SET is_active = $active, updated_at = $now WHERE id = $id;";
        command.With("$active", active ? 1 : 0).WithDate("$now", DateTime.Now).With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record(active ? "reactivated product" : "deactivated product",
            "Product", id, newValue: name, detail: $"{(active ? "reactivated" : "deactivated")} {name}");
    }

    /// <summary>Barcode uniqueness check for the product form, excluding the row being edited.</summary>
    public static bool BarcodeTaken(string barcode, int exceptId = 0)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM products WHERE barcode = $barcode AND id <> $id;";
        command.With("$barcode", barcode.Trim()).With("$id", exceptId);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// The next free internal barcode for a product that has none printed on it. Uses the
    /// 2xxxxxxxxxxx range, which EAN-13 reserves for in-store use, so a shop-made code can
    /// never collide with a manufacturer's.
    /// </summary>
    public static string NextInternalBarcode()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(MAX(CAST(barcode AS INTEGER)), 2000000000000)
            FROM products WHERE barcode GLOB '2[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]';
            """;
        var next = Convert.ToInt64(command.ExecuteScalar()) + 1;
        return next.ToString("D13");
    }

    // ------------------------------- Alerts -------------------------------

    public static List<StockItem> LowStock() =>
        List().Where(i => i.Status == StockStatus.LowStock).OrderBy(i => i.Stock).ToList();

    public static List<StockItem> OutOfStock() =>
        List().Where(i => i.Status == StockStatus.OutOfStock).OrderBy(i => i.Name).ToList();

    public static List<StockItem> Expiring(int withinDays = 30) =>
        List().Where(i => i.ExpiresOn is not null && i.DaysToExpiry <= withinDays)
              .OrderBy(i => i.ExpiresOn).ToList();
}
