using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>
/// Suppliers, deliveries and what is still owed to them.
///
/// A purchase and a payment are separate records on purpose. The purchase says what arrived
/// and what it cost; the payment says when money left. Adding 50 crates of milk on credit
/// increases stock and debt but no cash moves, and the dashboard has to be able to show
/// exactly that.
/// </summary>
public static class SupplierRepository
{
    // ------------------------------ Suppliers ------------------------------

    public static List<Supplier> List(bool includeInactive = false, string? search = null)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (!includeInactive) where.Add("s.is_active = 1");
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(s.name LIKE $q OR s.contact LIKE $q OR s.phone LIKE $q)");
            command.With("$q", $"%{search.Trim()}%");
        }

        // Only Received purchases count towards debt — a cancelled delivery is not owed for.
        command.CommandText = $"""
            SELECT s.id, s.name, s.contact, s.phone, s.email, s.address, s.note, s.is_active,
                   COALESCE((SELECT SUM(CAST(p.total AS REAL)) FROM purchases p
                             WHERE p.supplier_id = s.id AND p.status = 'Received'), 0),
                   COALESCE((SELECT SUM(CAST(sp.amount AS REAL)) FROM supplier_payments sp
                             WHERE sp.supplier_id = s.id), 0)
            FROM suppliers s
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY s.name;
            """;

        var suppliers = new List<Supplier>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            suppliers.Add(new Supplier
            {
                Id = reader.Int(0),
                Name = reader.Str(1),
                Contact = reader.Str(2),
                Phone = reader.Str(3),
                Email = reader.Str(4),
                Address = reader.Str(5),
                Note = reader.Str(6),
                IsActive = reader.Bool(7),
                TotalPurchased = (decimal)reader.GetDouble(8),
                TotalPaid = (decimal)reader.GetDouble(9),
            });
        }
        return suppliers;
    }

    public static int Create(Supplier supplier)
    {
        Session.Require(Permission.ManageSuppliers);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO suppliers (name, contact, phone, email, address, note, is_active, created_at)
            VALUES ($name, $contact, $phone, $email, $address, $note, 1, $now);
            SELECT last_insert_rowid();
            """;
        Bind(command, supplier);
        command.WithDate("$now", DateTime.Now);
        var id = Convert.ToInt32(command.ExecuteScalar());

        ActivityRepository.Record("added supplier", "Supplier", id, newValue: supplier.Name,
                                  detail: $"added supplier {supplier.Name}");
        return id;
    }

    public static void Update(Supplier supplier)
    {
        Session.Require(Permission.ManageSuppliers);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE suppliers SET name = $name, contact = $contact, phone = $phone,
                   email = $email, address = $address, note = $note
            WHERE id = $id;
            """;
        Bind(command, supplier);
        command.With("$id", supplier.Id);
        command.ExecuteNonQuery();

        ActivityRepository.Record("edited supplier", "Supplier", supplier.Id,
            newValue: supplier.Name, detail: $"edited supplier {supplier.Name}");
    }

    public static void SetActive(int id, string name, bool active)
    {
        Session.Require(Permission.ManageSuppliers);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE suppliers SET is_active = $active WHERE id = $id;";
        command.With("$active", active ? 1 : 0).With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record(active ? "reactivated supplier" : "deactivated supplier",
            "Supplier", id, newValue: name, detail: $"{(active ? "reactivated" : "deactivated")} supplier {name}");
    }

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, Supplier s) =>
        command.With("$name", s.Name).With("$contact", s.Contact).With("$phone", s.Phone)
               .With("$email", s.Email).With("$address", s.Address).With("$note", s.Note);

    // ------------------------------ Purchases ------------------------------

    /// <summary>
    /// Records a delivery: the invoice, its lines, the stock they add, and any money handed
    /// over at the door. All of it in one transaction, so a half-entered delivery cannot
    /// leave stock up and the invoice missing.
    /// </summary>
    public static int RecordPurchase(Purchase purchase, decimal amountPaidNow, bool addToStock = true)
    {
        Session.Require(Permission.ManagePurchases);
        if (purchase.Lines.Count == 0)
            throw new ArgumentException("A purchase needs at least one product line.");

        // Repricing the shelf is a different power from recording what arrived, so it is
        // asked for separately — and only when a line actually carries a new price.
        if (purchase.Lines.Any(l => l.SellPrice is > 0m))
            Session.Require(Permission.ManageProducts);

        var lines = CreateAnyNewProducts(purchase.Lines);

        var total = lines.Sum(l => l.LineTotal);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        int purchaseId;
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO purchases
                    (supplier_id, invoice_number, purchased_on, due_on, total, method, note,
                     status, received, created_by, created_at)
                VALUES ($supplierId, $invoice, $on, $due, $total, $method, $note,
                        'Received', $received, $by, $now);
                SELECT last_insert_rowid();
                """;
            insert.With("$supplierId", purchase.SupplierId)
                  .With("$invoice", purchase.InvoiceNumber)
                  .WithDate("$on", purchase.PurchasedOn)
                  .WithDate("$due", purchase.DueOn)
                  .WithMoney("$total", total)
                  .With("$method", purchase.Method)
                  .With("$note", purchase.Note)
                  .With("$received", addToStock ? 1 : 0)
                  .With("$by", Session.CurrentId)
                  .WithDate("$now", DateTime.Now);
            purchaseId = Convert.ToInt32(insert.ExecuteScalar());
        }

        foreach (var line in lines)
        {
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = """
                    INSERT INTO purchase_lines (purchase_id, product_id, name, quantity, unit_cost, line_total)
                    VALUES ($purchaseId, $productId, $name, $qty, $cost, $total);
                    """;
                insert.With("$purchaseId", purchaseId)
                      .With("$productId", line.ProductId)
                      .With("$name", line.Name)
                      .WithMoney("$qty", line.Quantity)
                      .WithMoney("$cost", line.UnitCost)
                      .WithMoney("$total", line.LineTotal);
                insert.ExecuteNonQuery();
            }

            if (!addToStock) continue;

            InventoryRepository.Move(line.ProductId, line.Quantity, StockReason.SupplierPurchase,
                reference: $"Purchase #{purchaseId}", unitCost: line.UnitCost, connection: connection);

            // The delivered cost becomes the product's cost, so COGS on the next sale uses
            // what this shop actually paid rather than a figure typed in months ago.
            using (var cost = connection.CreateCommand())
            {
                cost.CommandText = "UPDATE products SET cost = $cost WHERE id = $id;";
                cost.WithMoney("$cost", line.UnitCost).With("$id", line.ProductId);
                cost.ExecuteNonQuery();
            }

            // A new shelf price, when the owner set one on the line. In the same transaction
            // as the stock and the cost: a delivery that repriced half its products and then
            // failed would leave the shop selling at prices nobody chose.
            if (line.SellPrice is not { } sellPrice || sellPrice <= 0m) continue;

            decimal wasPrice;
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT price FROM products WHERE id = $id;";
                read.With("$id", line.ProductId);
                wasPrice = Db.ParseMoney(read.ExecuteScalar() as string);
            }

            if (wasPrice == sellPrice) continue;

            using (var price = connection.CreateCommand())
            {
                price.CommandText = "UPDATE products SET price = $price WHERE id = $id;";
                price.WithMoney("$price", sellPrice).With("$id", line.ProductId);
                price.ExecuteNonQuery();
            }

            ActivityRepository.Record("changed a price", "Product", line.ProductId,
                oldValue: $"{wasPrice:0.00}", newValue: $"{sellPrice:0.00}",
                detail: $"repriced {line.Name} on a delivery", connection: connection);
        }

        if (amountPaidNow > 0m)
            InsertPayment(connection, purchase.SupplierId, purchaseId, amountPaidNow,
                          purchase.PurchasedOn, purchase.Method, "Paid on delivery");

        ActivityRepository.Record("recorded supplier purchase", "Purchase", purchaseId,
            newValue: $"{total:0.00} DH",
            detail: $"recorded a {total:0.00} DH purchase from {purchase.SupplierName}",
            connection: connection);

        transaction.Commit();
        return purchaseId;
    }

    /// <summary>
    /// Turns lines typed as plain names into real products, and returns the lines pointing at
    /// them.
    ///
    /// A delivery is where a shop meets a product for the first time — the van brings
    /// something new and it has to go somewhere. Made before the transaction opens rather
    /// than inside it: a product created and then rolled back would be a product the shop
    /// typed in and lost, whereas one left behind by a failed delivery is simply a product
    /// with no stock, which is what a shop has before its first delivery anyway.
    /// </summary>
    private static List<PurchaseLine> CreateAnyNewProducts(List<PurchaseLine> lines)
    {
        if (!lines.Any(l => l.IsNew)) return lines;

        Session.Require(Permission.ManageProducts);

        var made = new List<PurchaseLine>(lines.Count);
        foreach (var line in lines)
        {
            if (!line.IsNew)
            {
                made.Add(line);
                continue;
            }

            var id = StockRepository.Create(new StockItem
            {
                // The code on the box when one was scanned, so the till finds it by scan from
                // the first sale. Only goods with no barcode of their own get an in-store one.
                Barcode = string.IsNullOrWhiteSpace(line.Barcode)
                    ? StockRepository.NextInternalBarcode()
                    : line.Barcode,
                Name = line.Name,
                Cost = line.UnitCost,
                // No selling price given means the shop has not decided yet. Cost is the
                // honest placeholder: it makes nothing, rather than pretending to.
                Price = line.SellPrice ?? line.UnitCost,
            });

            made.Add(new PurchaseLine
            {
                ProductId = id,
                Barcode = line.Barcode,
                Name = line.Name,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                SellPrice = line.SellPrice,
            });
        }

        return made;
    }

    public static List<Purchase> ListPurchases(DateRange? range = null, int? supplierId = null,
                                               PaymentStatus? status = null, int limit = 300)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (range is { } r)
        {
            where.Add("p.purchased_on >= $from AND p.purchased_on < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (supplierId is { } sid)
        {
            where.Add("p.supplier_id = $sid");
            command.With("$sid", sid);
        }

        command.CommandText = $"""
            SELECT p.id, p.supplier_id, s.name, p.invoice_number, p.purchased_on, p.due_on,
                   p.total, p.method, p.note, p.status, p.received,
                   COALESCE((SELECT SUM(CAST(sp.amount AS REAL)) FROM supplier_payments sp
                             WHERE sp.purchase_id = p.id), 0)
            FROM purchases p
            JOIN suppliers s ON s.id = p.supplier_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY p.purchased_on DESC, p.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var purchases = new List<Purchase>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                purchases.Add(new Purchase
                {
                    Id = reader.Int(0),
                    SupplierId = reader.Int(1),
                    SupplierName = reader.Str(2),
                    InvoiceNumber = reader.Str(3),
                    PurchasedOn = reader.Date(4),
                    DueOn = reader.DateOrNull(5),
                    Total = reader.Dec(6),
                    Method = reader.Str(7),
                    Note = reader.Str(8),
                    Status = reader.Str(9),
                    Received = reader.Bool(10),
                    Paid = (decimal)reader.GetDouble(11),
                });
            }
        }

        return status is { } want ? purchases.Where(p => p.PaymentStatus == want).ToList() : purchases;
    }

    public static List<PurchaseLine> ListPurchaseLines(int purchaseId)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, product_id, name, quantity, unit_cost FROM purchase_lines
            WHERE purchase_id = $id ORDER BY id;
            """;
        command.With("$id", purchaseId);

        var lines = new List<PurchaseLine>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            lines.Add(new PurchaseLine
            {
                Id = reader.Int(0),
                ProductId = reader.Int(1),
                Name = reader.Str(2),
                Quantity = reader.Dec(3),
                UnitCost = reader.Dec(4),
            });
        }
        return lines;
    }

    /// <summary>
    /// What the shop buys from this supplier, rolled up by product.
    ///
    /// The per-delivery lines answer "what came in the van last Tuesday"; this answers the
    /// question the owner actually negotiates on — what do I buy from them, how much of it,
    /// and what am I paying. The last price is carried separately from the average because a
    /// supplier who has quietly put a price up is exactly what this is for.
    ///
    /// Cancelled deliveries are left out: they were reversed out of stock and out of the
    /// debt, so counting them here would say the shop buys more than it does.
    /// </summary>
    public static List<SupplierGoods> WhatWeBuy(int supplierId)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.product_id,
                   l.name,
                   COALESCE(SUM(CAST(l.quantity AS REAL)), 0),
                   COALESCE(SUM(CAST(l.line_total AS REAL)), 0),
                   COUNT(DISTINCT p.id),
                   MAX(p.purchased_on)
            FROM purchase_lines l
            JOIN purchases p ON p.id = l.purchase_id
            WHERE p.supplier_id = $id AND p.status <> 'Cancelled'
            GROUP BY l.product_id, l.name
            ORDER BY 4 DESC;
            """;
        command.With("$id", supplierId);

        var goods = new List<SupplierGoods>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                goods.Add(new SupplierGoods
                {
                    ProductId = reader.Int(0),
                    Name = reader.Str(1),
                    Quantity = (decimal)reader.GetDouble(2),
                    TotalCost = (decimal)reader.GetDouble(3),
                    Deliveries = reader.Int(4),
                    LastBought = Db.ParseStamp(reader.Str(5)),
                });
            }
        }

        // The most recent unit cost per product, so a price rise is visible next to the
        // average rather than hidden inside it.
        foreach (var item in goods)
        {
            using var latest = connection.CreateCommand();
            latest.CommandText = """
                SELECT l.unit_cost FROM purchase_lines l
                JOIN purchases p ON p.id = l.purchase_id
                WHERE p.supplier_id = $id AND l.product_id = $product AND p.status <> 'Cancelled'
                ORDER BY p.purchased_on DESC, l.id DESC LIMIT 1;
                """;
            latest.With("$id", supplierId).With("$product", item.ProductId);
            item.LastUnitCost = Db.ParseMoney(latest.ExecuteScalar() as string);
        }

        return goods;
    }

    /// <summary>
    /// Cancels a delivery — reverses the stock it added and stops it counting towards debt.
    /// The record itself stays, marked Cancelled, because a supplier invoice that vanished
    /// is exactly the kind of hole this system exists to prevent.
    /// </summary>
    public static void CancelPurchase(int purchaseId, string reason)
    {
        Session.Require(Permission.ManagePurchases);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        bool received;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT received, status FROM purchases WHERE id = $id;";
            read.With("$id", purchaseId);
            using var reader = read.ExecuteReader();
            if (!reader.Read()) return;
            received = reader.Bool(0);
            if (reader.Str(1) == "Cancelled") return;   // already done; do not double-reverse
        }

        if (received)
        {
            foreach (var line in ListPurchaseLines(purchaseId))
                InventoryRepository.Move(line.ProductId, -line.Quantity, StockReason.SupplierReturn,
                    reference: $"Purchase #{purchaseId} cancelled", note: reason,
                    unitCost: line.UnitCost, connection: connection);
        }

        using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE purchases SET status = 'Cancelled', received = 0 WHERE id = $id;";
            update.With("$id", purchaseId);
            update.ExecuteNonQuery();
        }

        ActivityRepository.Record("cancelled supplier purchase", "Purchase", purchaseId,
            newValue: reason, detail: $"cancelled purchase #{purchaseId}", connection: connection);

        transaction.Commit();
    }

    // ------------------------------- Payments -------------------------------

    /// <summary>Records money paid to a supplier. Never edits a previous payment.</summary>
    public static void Pay(int supplierId, string supplierName, decimal amount, DateTime paidOn,
                           string method = "Cash", string note = "", int? purchaseId = null)
    {
        Session.Require(Permission.ManagePurchases);
        if (amount <= 0m) throw new ArgumentException("A payment must be greater than zero.", nameof(amount));

        using var connection = Database.Open();
        InsertPayment(connection, supplierId, purchaseId, amount, paidOn, method, note);

        ActivityRepository.Record("recorded a supplier payment", "Supplier", supplierId,
            newValue: $"{amount:0.00} DH",
            detail: $"recorded a {amount:0.00} DH payment to {supplierName}", connection: connection);
    }

    private static void InsertPayment(Microsoft.Data.Sqlite.SqliteConnection connection,
                                      int supplierId, int? purchaseId, decimal amount,
                                      DateTime paidOn, string method, string note)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO supplier_payments
                (supplier_id, purchase_id, amount, paid_on, method, note, created_by, created_at)
            VALUES ($supplierId, $purchaseId, $amount, $on, $method, $note, $by, $now);
            """;
        command.With("$supplierId", supplierId)
               .With("$purchaseId", purchaseId)
               .WithMoney("$amount", amount)
               .WithDate("$on", paidOn)
               .With("$method", method)
               .With("$note", note)
               .With("$by", Session.CurrentId)
               .WithDate("$now", DateTime.Now);
        command.ExecuteNonQuery();
    }

    public static List<SupplierPayment> ListPayments(DateRange? range = null, int? supplierId = null,
                                                     int limit = 300)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (range is { } r)
        {
            where.Add("sp.paid_on >= $from AND sp.paid_on < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (supplierId is { } sid)
        {
            where.Add("sp.supplier_id = $sid");
            command.With("$sid", sid);
        }

        command.CommandText = $"""
            SELECT sp.id, sp.supplier_id, s.name, sp.purchase_id, sp.amount, sp.paid_on,
                   sp.method, sp.note
            FROM supplier_payments sp
            JOIN suppliers s ON s.id = sp.supplier_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY sp.paid_on DESC, sp.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var payments = new List<SupplierPayment>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            payments.Add(new SupplierPayment
            {
                Id = reader.Int(0),
                SupplierId = reader.Int(1),
                SupplierName = reader.Str(2),
                PurchaseId = reader.IsDBNull(3) ? null : reader.Int(3),
                Amount = reader.Dec(4),
                PaidOn = reader.Date(5),
                Method = reader.Str(6),
                Note = reader.Str(7),
            });
        }
        return payments;
    }

    /// <summary>Total still owed across every supplier — the dashboard's "money owed" figure.</summary>
    public static decimal TotalOwed() => List().Sum(s => s.Owed);
}
