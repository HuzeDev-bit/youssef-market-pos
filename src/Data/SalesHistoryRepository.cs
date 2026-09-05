using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>One line of a sale as the history page shows it.</summary>
public sealed class SaleDetailLine
{
    public int Id { get; init; }
    public int? ProductId { get; init; }
    public required string Name { get; init; }
    public decimal Quantity { get; init; }
    public decimal ReturnedQty { get; init; }
    public Unit Unit { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal UnitCost { get; init; }
    public decimal LineTotal { get; init; }
    public decimal Returnable => Math.Max(0m, Quantity - ReturnedQty);
}

/// <summary>A sale with everything the back office needs to show and to refund it.</summary>
public sealed class SaleDetail
{
    public long Id { get; init; }
    public int InvoiceNumber { get; init; }
    public DateTime SoldAt { get; init; }
    public string CashierName { get; init; } = string.Empty;
    public decimal Subtotal { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public decimal CostTotal { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal Refunded { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public SaleStatus Status { get; init; }
    public string Note { get; init; } = string.Empty;
    public List<SaleDetailLine> Lines { get; init; } = new();

    public decimal NetTotal => Total - Refunded;
    public decimal Profit => NetTotal - CostTotal;
    public int LineCount => Lines.Count;
}

/// <summary>
/// Sales history, refunds and product performance.
///
/// A refund never deletes the sale. It writes a return record, moves the refunded amount onto
/// the sale, and — when the goods came back in sellable condition — puts the stock back. The
/// original transaction stays exactly as it was rung up.
/// </summary>
public static class SalesHistoryRepository
{
    public static List<SaleSummaryEx> List(DateRange? range = null, string? search = null,
                                           int? workerId = null, PaymentMethod? method = null,
                                           int? productId = null, int? categoryId = null,
                                           int limit = 400)
    {
        // A cashier may look at their own sales; anything wider needs SeeAllSales.
        if (!Session.Can(Permission.SeeAllSales))
        {
            Session.Require(Permission.SeeOwnSales);
            workerId = Session.CurrentId ?? -1;
        }

        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string> { "s.is_voided = 0" };
        if (range is { } r)
        {
            where.Add("s.sold_at >= $from AND s.sold_at < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (workerId is { } wid)
        {
            where.Add("s.worker_id = $wid");
            command.With("$wid", wid);
        }
        if (method is { } m)
        {
            where.Add("s.payment_method = $method");
            command.With("$method", m.ToString());
        }
        if (productId is { } pid)
        {
            where.Add("EXISTS (SELECT 1 FROM sale_lines l WHERE l.sale_id = s.id AND l.product_id = $pid)");
            command.With("$pid", pid);
        }
        if (categoryId is { } cid)
        {
            where.Add("""
                EXISTS (SELECT 1 FROM sale_lines l JOIN products p ON p.id = l.product_id
                        WHERE l.sale_id = s.id AND p.category_id = $cid)
                """);
            command.With("$cid", cid);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            if (int.TryParse(q, out var number))
            {
                where.Add("s.invoice_number = $number");
                command.With("$number", number);
            }
            else
            {
                where.Add("""
                    (s.worker_name LIKE $q
                     OR EXISTS (SELECT 1 FROM sale_lines l WHERE l.sale_id = s.id AND l.name LIKE $q))
                    """);
                command.With("$q", $"%{q}%");
            }
        }

        command.CommandText = $"""
            SELECT s.invoice_number, s.sold_at, s.total, s.discount_amount, s.payment_method,
                   (SELECT COUNT(*) FROM sale_lines l WHERE l.sale_id = s.id),
                   s.cost_total, s.worker_name, s.status, s.refunded
            FROM sales s
            WHERE {string.Join(" AND ", where)}
            ORDER BY s.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var sales = new List<SaleSummaryEx>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sales.Add(new SaleSummaryEx
            {
                InvoiceNumber = reader.Int(0),
                SoldAt = reader.Date(1),
                Total = reader.Dec(2),
                DiscountAmount = reader.Dec(3),
                PaymentMethod = Enum.TryParse<PaymentMethod>(reader.Str(4), out var pm) ? pm : PaymentMethod.Cash,
                LineCount = reader.Int(5),
                CostTotal = reader.Dec(6),
                CashierName = reader.Str(7),
                Status = Enum.TryParse<SaleStatus>(reader.Str(8), out var st) ? st : SaleStatus.Completed,
                Refunded = reader.Dec(9),
            });
        }
        return sales;
    }

    public static SaleDetail? Find(int invoiceNumber)
    {
        using var connection = Database.Open();

        SaleDetail head;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, invoice_number, sold_at, worker_name, subtotal, tax, total,
                       cost_total, discount_amount, refunded, payment_method, status, note
                FROM sales WHERE invoice_number = $invoice;
                """;
            command.With("$invoice", invoiceNumber);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;

            head = new SaleDetail
            {
                Id = reader.GetInt64(0),
                InvoiceNumber = reader.Int(1),
                SoldAt = reader.Date(2),
                CashierName = reader.Str(3),
                Subtotal = reader.Dec(4),
                Tax = reader.Dec(5),
                Total = reader.Dec(6),
                CostTotal = reader.Dec(7),
                DiscountAmount = reader.Dec(8),
                Refunded = reader.Dec(9),
                PaymentMethod = Enum.TryParse<PaymentMethod>(reader.Str(10), out var pm) ? pm : PaymentMethod.Cash,
                Status = Enum.TryParse<SaleStatus>(reader.Str(11), out var st) ? st : SaleStatus.Completed,
                Note = reader.Str(12),
            };
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, product_id, name, quantity, returned_qty, unit, unit_price,
                       unit_cost, line_total
                FROM sale_lines WHERE sale_id = $saleId ORDER BY id;
                """;
            command.With("$saleId", head.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                head.Lines.Add(new SaleDetailLine
                {
                    Id = reader.Int(0),
                    ProductId = reader.IsDBNull(1) ? null : reader.Int(1),
                    Name = reader.Str(2),
                    Quantity = reader.Dec(3),
                    ReturnedQty = reader.Dec(4),
                    Unit = reader.Str(5) == nameof(Unit.Kg) ? Unit.Kg : Unit.Each,
                    UnitPrice = reader.Dec(6),
                    UnitCost = reader.Dec(7),
                    LineTotal = reader.Dec(8),
                });
            }
        }

        return head;
    }

    /// <summary>
    /// Refunds part or all of a sale.
    ///
    /// <paramref name="restock"/> decides whether the goods go back on the shelf: a returned
    /// unopened bottle does, a broken one does not, and the difference is the whole reason
    /// this is a question rather than an assumption.
    /// </summary>
    public static void Refund(int invoiceNumber, IReadOnlyList<(int SaleLineId, decimal Quantity)> items,
                              string reason, bool restock)
    {
        Session.Require(Permission.Refund);

        var sale = Find(invoiceNumber) ?? throw new InvalidOperationException("That sale no longer exists.");
        if (items.Count == 0) throw new ArgumentException("Choose at least one line to return.");

        var lines = sale.Lines.ToDictionary(l => l.Id);
        var refundTotal = 0m;

        foreach (var (lineId, quantity) in items)
        {
            if (!lines.TryGetValue(lineId, out var line))
                throw new InvalidOperationException("That line is not part of this sale.");
            if (quantity <= 0m || quantity > line.Returnable)
                throw new InvalidOperationException(
                    $"Cannot return {quantity:0.###} of {line.Name}; only {line.Returnable:0.###} is left to return.");

            refundTotal += Math.Round(quantity * line.UnitPrice, 2);
        }

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        long returnId;
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO sale_returns (sale_id, returned_at, amount, reason, restock, worker_id, note)
                VALUES ($saleId, $at, $amount, $reason, $restock, $workerId, '');
                SELECT last_insert_rowid();
                """;
            insert.With("$saleId", sale.Id).WithDate("$at", DateTime.Now)
                  .WithMoney("$amount", refundTotal).With("$reason", reason)
                  .With("$restock", restock ? 1 : 0).With("$workerId", Session.CurrentId);
            returnId = Convert.ToInt64(insert.ExecuteScalar());
        }

        foreach (var (lineId, quantity) in items)
        {
            var line = lines[lineId];

            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = """
                    INSERT INTO sale_return_lines
                        (return_id, sale_line_id, product_id, quantity, unit_price, unit_cost, line_total)
                    VALUES ($returnId, $lineId, $productId, $qty, $price, $cost, $total);
                    """;
                insert.With("$returnId", returnId).With("$lineId", lineId)
                      .With("$productId", line.ProductId)
                      .WithMoney("$qty", quantity).WithMoney("$price", line.UnitPrice)
                      .WithMoney("$cost", line.UnitCost)
                      .WithMoney("$total", Math.Round(quantity * line.UnitPrice, 2));
                insert.ExecuteNonQuery();
            }

            using (var update = connection.CreateCommand())
            {
                update.CommandText = """
                    UPDATE sale_lines
                    SET returned_qty = CAST(CAST(returned_qty AS REAL) + $qty AS TEXT)
                    WHERE id = $id;
                    """;
                update.With("$qty", (double)quantity).With("$id", lineId);
                update.ExecuteNonQuery();
            }

            if (restock && line.ProductId is { } pid)
                InventoryRepository.Move(pid, quantity, StockReason.CustomerReturn,
                    reference: $"Return on sale #{invoiceNumber}", note: reason,
                    unitCost: line.UnitCost, connection: connection);
        }

        var totalRefunded = sale.Refunded + refundTotal;
        var status = totalRefunded >= sale.Total ? SaleStatus.Refunded : SaleStatus.PartlyRefunded;

        using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE sales SET refunded = $refunded, status = $status WHERE id = $id;";
            update.WithMoney("$refunded", totalRefunded).With("$status", status.ToString())
                  .With("$id", sale.Id);
            update.ExecuteNonQuery();
        }

        ActivityRepository.Record("refunded a sale", "Sale", invoiceNumber,
            oldValue: $"{sale.Total:0.00} DH", newValue: $"{refundTotal:0.00} DH refunded",
            detail: $"refunded {refundTotal:0.00} DH on sale #{invoiceNumber} ({reason})",
            connection: connection);

        transaction.Commit();
    }

    /// <summary>
    /// Cancels a sale outright — the whole thing was rung up in error. Stock goes back and the
    /// sale stops counting towards revenue, but the record stays, marked Cancelled.
    /// </summary>
    public static void Cancel(int invoiceNumber, string reason)
    {
        Session.Require(Permission.Refund);

        var sale = Find(invoiceNumber) ?? throw new InvalidOperationException("That sale no longer exists.");
        if (sale.Status == SaleStatus.Cancelled) return;

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var line in sale.Lines.Where(l => l.ProductId is not null))
            InventoryRepository.Move(line.ProductId!.Value, line.Quantity - line.ReturnedQty,
                StockReason.CustomerReturn, reference: $"Sale #{invoiceNumber} cancelled",
                note: reason, unitCost: line.UnitCost, connection: connection);

        using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE sales SET status = 'Cancelled', refunded = total,
                       note = CASE WHEN note = '' THEN $reason ELSE note || ' | ' || $reason END
                WHERE id = $id;
                """;
            update.With("$reason", reason).With("$id", sale.Id);
            update.ExecuteNonQuery();
        }

        ActivityRepository.Record("cancelled a sale", "Sale", invoiceNumber,
            oldValue: $"{sale.Total:0.00} DH", newValue: "cancelled",
            detail: $"cancelled sale #{invoiceNumber} ({reason})", connection: connection);

        transaction.Commit();
    }

    // --------------------------- Product performance ---------------------------

    public sealed class ProductStat
    {
        public int ProductId { get; init; }
        public required string Name { get; init; }
        public string Category { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public decimal Revenue { get; init; }
        public decimal Cost { get; init; }
        public decimal Profit => Revenue - Cost;
        public decimal MarginPercent => Revenue <= 0m ? 0m : Math.Round(Profit / Revenue * 100m, 1);
    }

    /// <summary>
    /// Units sold, revenue and profit per product, net of anything returned. Cancelled sales
    /// are excluded entirely — they did not happen.
    /// </summary>
    public static List<ProductStat> ProductPerformance(DateRange range, int limit = 200)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.product_id, l.name, COALESCE(c.name, ''),
                   COALESCE(SUM(CAST(l.quantity AS REAL) - CAST(l.returned_qty AS REAL)), 0),
                   COALESCE(SUM((CAST(l.quantity AS REAL) - CAST(l.returned_qty AS REAL))
                                * CAST(l.unit_price AS REAL)), 0),
                   COALESCE(SUM((CAST(l.quantity AS REAL) - CAST(l.returned_qty AS REAL))
                                * CAST(l.unit_cost AS REAL)), 0)
            FROM sale_lines l
            JOIN sales s ON s.id = l.sale_id
            LEFT JOIN products p ON p.id = l.product_id
            LEFT JOIN categories c ON c.id = p.category_id
            WHERE s.is_voided = 0 AND s.status <> 'Cancelled'
              AND s.sold_at >= $from AND s.sold_at < $to
            GROUP BY l.product_id, l.name
            ORDER BY 5 DESC LIMIT $limit;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To).With("$limit", limit);

        var stats = new List<ProductStat>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            stats.Add(new ProductStat
            {
                ProductId = reader.Int(0),
                Name = reader.Str(1),
                Category = reader.Str(2),
                Quantity = (decimal)reader.GetDouble(3),
                Revenue = (decimal)reader.GetDouble(4),
                Cost = (decimal)reader.GetDouble(5),
            });
        }
        return stats;
    }

    /// <summary>Per-cashier takings for the period — the cashier performance report.</summary>
    /// <summary>
    /// The workers who actually rang something up in this period. Used to keep a cashier who
    /// has since left selectable while their sales are on screen, without carrying every
    /// former member of staff in the filter forever.
    /// </summary>
    public static HashSet<int> WhoSoldIn(DateRange range)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT worker_id FROM sales
            WHERE worker_id IS NOT NULL
              AND sold_at >= $from AND sold_at < $to;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To);

        var ids = new HashSet<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.Int(0));
        return ids;
    }

    public static List<(string Cashier, int Sales, decimal Revenue, decimal Discounts)> CashierPerformance(
        DateRange range)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT CASE WHEN worker_name = '' THEN 'Unassigned' ELSE worker_name END,
                   COUNT(*), {Db.Sum("total")}, {Db.Sum("discount_amount")}
            FROM sales
            WHERE is_voided = 0 AND status <> 'Cancelled'
              AND sold_at >= $from AND sold_at < $to
            GROUP BY worker_name ORDER BY 3 DESC;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To);

        var rows = new List<(string, int, decimal, decimal)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add((reader.GetString(0), reader.GetInt32(1),
                      (decimal)reader.GetDouble(2), (decimal)reader.GetDouble(3)));
        return rows;
    }
}

/// <summary>The till's sale summary plus the back-office columns.</summary>
public sealed class SaleSummaryEx
{
    public int InvoiceNumber { get; init; }
    public DateTime SoldAt { get; init; }
    public decimal Total { get; init; }
    public decimal CostTotal { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal Refunded { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public int LineCount { get; init; }
    public string CashierName { get; init; } = string.Empty;
    public SaleStatus Status { get; init; }

    public decimal NetTotal => Total - Refunded;
    public decimal Profit => NetTotal - CostTotal;
    public bool IsCancelled => Status == SaleStatus.Cancelled;

    public string StatusLabel => Status switch
    {
        SaleStatus.Refunded => "Refunded",
        SaleStatus.PartlyRefunded => "Partly refunded",
        SaleStatus.Cancelled => "Cancelled",
        _ => "Completed",
    };

    public string TimeLabel => SoldAt.ToString("d MMM, HH:mm");
    public string CashierLabel => string.IsNullOrWhiteSpace(CashierName) ? "—" : CashierName;

    /// <summary>
    /// Profit, or a dash when it cannot be known. A sale whose products had no purchase price
    /// recorded has a cost of zero, which would make the whole takings look like margin — so
    /// the column says nothing rather than something false.
    /// </summary>
    public string ProfitLabel => CostTotal <= 0m && NetTotal > 0m
        ? "—"
        : $"{Profit:N2} DH";
}
