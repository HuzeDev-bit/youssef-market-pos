using MarketPos.Models;
using MarketPos.Services;
using Microsoft.Data.Sqlite;

namespace MarketPos.Data;

/// <summary>
/// The only way stock is allowed to change.
///
/// Every caller goes through <see cref="Move"/>, which updates the product and writes the
/// movement row in the same statement batch. Nothing anywhere else writes products.stock —
/// that is what keeps the count and its history from disagreeing.
/// </summary>
public static class InventoryRepository
{
    /// <summary>
    /// Applies a signed change and records why. Returns the new quantity.
    ///
    /// Pass an existing <paramref name="connection"/> when this is part of a larger
    /// transaction (a sale, a supplier delivery) so stock and the reason it moved commit
    /// together or not at all.
    /// </summary>
    public static decimal Move(
        int productId,
        decimal quantity,
        StockReason reason,
        string reference = "",
        string note = "",
        decimal? unitCost = null,
        SqliteConnection? connection = null)
    {
        var own = connection is null;
        var db = connection ?? Database.Open();
        SqliteTransaction? transaction = null;

        try
        {
            if (own) transaction = db.BeginTransaction();

            decimal before, cost;
            using (var read = db.CreateCommand())
            {
                read.CommandText = "SELECT stock, cost FROM products WHERE id = $id;";
                read.With("$id", productId);
                using var reader = read.ExecuteReader();
                if (!reader.Read())
                    throw new InvalidOperationException($"No product with id {productId}.");
                before = reader.Dec(0);
                cost = reader.Dec(1);
            }

            var after = before + quantity;

            using (var write = db.CreateCommand())
            {
                write.CommandText = "UPDATE products SET stock = $stock, updated_at = $now WHERE id = $id;";
                write.WithMoney("$stock", after).WithDate("$now", DateTime.Now).With("$id", productId);
                write.ExecuteNonQuery();
            }

            using (var movement = db.CreateCommand())
            {
                movement.CommandText = """
                    INSERT INTO stock_movements
                        (product_id, moved_at, reason, quantity, before_qty, after_qty,
                         unit_cost, reference, worker_id, note)
                    VALUES ($productId, $at, $reason, $qty, $before, $after,
                            $cost, $reference, $workerId, $note);
                    """;
                movement.With("$productId", productId)
                        .WithDate("$at", DateTime.Now)
                        .With("$reason", reason.ToString())
                        .WithMoney("$qty", quantity)
                        .WithMoney("$before", before)
                        .WithMoney("$after", after)
                        .WithMoney("$cost", unitCost ?? cost)
                        .With("$reference", reference)
                        .With("$workerId", Session.CurrentId)
                        .With("$note", note);
                movement.ExecuteNonQuery();
            }

            transaction?.Commit();
            return after;
        }
        finally
        {
            transaction?.Dispose();
            if (own) db.Dispose();
        }
    }

    /// <summary>
    /// Records a loss — damaged, expired, stolen, used in the shop. The quantity leaves
    /// sellable stock and the movement carries the cost, so the loss can be valued later.
    /// </summary>
    public static void RecordLoss(int productId, string productName, decimal quantity,
                                  StockReason reason, string note = "")
    {
        Session.Require(Permission.ManageInventory);
        if (quantity <= 0m) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));

        Move(productId, -quantity, reason, reference: "Loss", note: note);
        ActivityRepository.Record("recorded stock loss", "Product", productId,
            newValue: $"-{quantity:0.###}",
            detail: $"recorded {quantity:0.###} of {productName} as {reason}");
    }

    /// <summary>
    /// Sets stock to an exact counted figure. Used after a shelf count; the difference is
    /// stored as a ManualCorrection so the adjustment itself is visible in the history.
    /// </summary>
    public static void SetCount(int productId, string productName, decimal counted, string note = "")
    {
        Session.Require(Permission.ManageInventory);

        using var connection = Database.Open();
        decimal before;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT stock FROM products WHERE id = $id;";
            read.With("$id", productId);
            before = Db.ParseMoney(read.ExecuteScalar() as string);
        }

        var delta = counted - before;
        if (delta == 0m) return;

        Move(productId, delta, StockReason.ManualCorrection, reference: "Stock count", note: note,
             connection: connection);
        ActivityRepository.Record("changed stock", "Product", productId,
            oldValue: before.ToString("0.###"), newValue: counted.ToString("0.###"),
            detail: $"changed {productName} stock", connection: connection);
    }

    public static List<StockMovement> ListMovements(DateRange? range = null, int? productId = null,
                                                    StockReason? reason = null, int limit = 400)
    {
        Session.Require(Permission.SeeStockMovements);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (range is { } r)
        {
            where.Add("m.moved_at >= $from AND m.moved_at < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (productId is { } pid)
        {
            where.Add("m.product_id = $pid");
            command.With("$pid", pid);
        }
        if (reason is { } rs)
        {
            where.Add("m.reason = $reason");
            command.With("$reason", rs.ToString());
        }

        command.CommandText = $"""
            SELECT m.id, m.product_id, p.name, m.moved_at, m.reason, m.quantity,
                   m.before_qty, m.after_qty, m.unit_cost, m.reference,
                   COALESCE(w.name, ''), m.note
            FROM stock_movements m
            JOIN products p ON p.id = m.product_id
            LEFT JOIN workers w ON w.id = m.worker_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY m.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var movements = new List<StockMovement>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            movements.Add(new StockMovement
            {
                Id = reader.Int(0),
                ProductId = reader.Int(1),
                ProductName = reader.Str(2),
                MovedAt = reader.Date(3),
                Reason = Enum.TryParse<StockReason>(reader.Str(4), out var rr) ? rr : StockReason.ManualCorrection,
                Quantity = reader.Dec(5),
                BeforeQty = reader.Dec(6),
                AfterQty = reader.Dec(7),
                UnitCost = reader.Dec(8),
                Reference = reader.Str(9),
                WorkerName = reader.Str(10),
                Note = reader.Str(11),
            });
        }
        return movements;
    }

    /// <summary>Value destroyed in the period, by reason — the inventory-loss report.</summary>
    public static List<(StockReason Reason, decimal Quantity, decimal Value)> LossesByReason(DateRange range)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT reason,
                   COALESCE(SUM(-CAST(quantity AS REAL)), 0),
                   COALESCE(SUM(-CAST(quantity AS REAL) * CAST(unit_cost AS REAL)), 0)
            FROM stock_movements
            WHERE moved_at >= $from AND moved_at < $to
              AND reason IN ('Damaged','Expired','Lost','Stolen','InternalUse')
            GROUP BY reason ORDER BY 3 DESC;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To);

        var rows = new List<(StockReason, decimal, decimal)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((
                Enum.TryParse<StockReason>(reader.GetString(0), out var r) ? r : StockReason.Damaged,
                (decimal)reader.GetDouble(1),
                (decimal)reader.GetDouble(2)));
        }
        return rows;
    }

    /// <summary>Total cost value of everything currently on the shelves.</summary>
    public static (decimal CostValue, decimal RetailValue, int Lines) TotalValue()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(CAST(stock AS REAL) * CAST(cost AS REAL)), 0),
                   COALESCE(SUM(CAST(stock AS REAL) * CAST(price AS REAL)), 0),
                   COUNT(*)
            FROM products WHERE is_active = 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return (0m, 0m, 0);
        return ((decimal)reader.GetDouble(0), (decimal)reader.GetDouble(1), reader.GetInt32(2));
    }
}
