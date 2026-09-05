using System.Globalization;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>Writes completed sales and reads them back for reprinting.</summary>
public static class SaleRepository
{
    /// <summary>
    /// Persists the sale and returns its invoice number. The number is allocated inside the
    /// same transaction as the insert, so the sequence stays gapless and unique.
    /// </summary>
    public static int Save(
        IReadOnlyList<SaleItem> lines,
        decimal grossBeforeDiscount,
        DiscountKind discountKind,
        decimal discountValue,
        decimal discountAmount,
        decimal subtotal,
        decimal tax,
        decimal total,
        PaymentMethod method,
        decimal amountTendered,
        SaleOrigin? origin = null)
    {
        using var connection = Database.Open();

        // A sale handed over by a till may already be here: the till retries anything it did
        // not get an answer for. Recognising it is the whole point of the reference, and it
        // has to happen before the transaction so a duplicate costs nothing.
        if (origin is { TillReference.Length: > 0 })
        {
            using var seen = connection.CreateCommand();
            seen.CommandText = "SELECT invoice_number FROM sales WHERE till_reference = $ref;";
            seen.Parameters.AddWithValue("$ref", origin.TillReference);

            if (seen.ExecuteScalar() is { } already && already != DBNull.Value)
                return Convert.ToInt32(already);
        }

        using var transaction = connection.BeginTransaction();

        using var next = connection.CreateCommand();
        next.CommandText = "SELECT COALESCE(MAX(invoice_number), 0) + 1 FROM sales;";
        var invoiceNumber = Convert.ToInt32(next.ExecuteScalar());

        // Cost is read now and frozen onto each line. Re-pricing a product next month must
        // not be able to rewrite this month's profit.
        var costs = ReadCosts(connection, lines);
        var costTotal = lines.Sum(l => Math.Round(l.Quantity * costs.GetValueOrDefault(l.Product.Id), 2));
        // A sale from a till belongs to whoever rang it up there, not to whoever is signed in
        // on this machine, and to the moment it happened rather than the moment it arrived.
        var soldAt = origin?.SoldAt ?? DateTime.Now;
        var workerId = origin is null ? Session.CurrentId : origin.WorkerId;
        var workerName = origin?.WorkerName ?? Session.CurrentName;

        // Shifts are a thing that happens at this machine, so a sale from elsewhere joins none.
        var shiftId = origin is null ? ShiftRepository.OpenShift(Session.CurrentId)?.Id : null;

        // Every sale taken on a networked till carries a reference this machine minted, and
        // that reference is what makes handing it over safe to retry. A shop with one computer
        // mints none: the column stays empty, which is what the unique index expects.
        var tillReference = origin?.TillReference
                            ?? (ShopLink.IsConfigured ? ShopLink.NewReference() : string.Empty);

        using var sale = connection.CreateCommand();
        sale.CommandText = @"
            INSERT INTO sales (invoice_number, sold_at, subtotal, tax, total,
                               payment_method, amount_tendered, change_given,
                               gross_before_discount, discount_kind, discount_value, discount_amount,
                               cost_total, worker_id, worker_name, shift_id, status,
                               till_reference)
            VALUES ($invoice, $soldAt, $subtotal, $tax, $total,
                    $method, $tendered, $change,
                    $gross, $dKind, $dValue, $dAmount,
                    $costTotal, $workerId, $workerName, $shiftId, 'Completed',
                    $tillReference);
            SELECT last_insert_rowid();";
        sale.Parameters.AddWithValue("$costTotal", Money(costTotal));
        sale.Parameters.AddWithValue("$workerId", (object?)workerId ?? DBNull.Value);
        sale.Parameters.AddWithValue("$workerName", workerName);
        sale.Parameters.AddWithValue("$tillReference", tillReference);
        sale.Parameters.AddWithValue("$shiftId", (object?)shiftId ?? DBNull.Value);
        sale.Parameters.AddWithValue("$invoice", invoiceNumber);
        sale.Parameters.AddWithValue("$soldAt", soldAt.ToString("O", CultureInfo.InvariantCulture));
        sale.Parameters.AddWithValue("$subtotal", Money(subtotal));
        sale.Parameters.AddWithValue("$tax", Money(tax));
        sale.Parameters.AddWithValue("$total", Money(total));
        sale.Parameters.AddWithValue("$method", method.ToString());
        sale.Parameters.AddWithValue("$tendered", Money(amountTendered));
        sale.Parameters.AddWithValue("$change", Money(Math.Max(0m, amountTendered - total)));
        sale.Parameters.AddWithValue("$gross", Money(grossBeforeDiscount));
        sale.Parameters.AddWithValue("$dKind", discountKind.ToString());
        sale.Parameters.AddWithValue("$dValue", Money(discountValue));
        sale.Parameters.AddWithValue("$dAmount", Money(discountAmount));
        var saleId = Convert.ToInt64(sale.ExecuteScalar());

        foreach (var line in lines)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO sale_lines (sale_id, product_id, barcode, name, unit,
                                        unit_price, quantity, tax_rate, line_total, unit_cost)
                VALUES ($saleId, $productId, $barcode, $name, $unit,
                        $unitPrice, $quantity, $taxRate, $lineTotal, $unitCost);";
            insert.Parameters.AddWithValue("$saleId", saleId);
            insert.Parameters.AddWithValue("$productId",
                line.Product.Id > 0 ? line.Product.Id : (object)DBNull.Value);
            insert.Parameters.AddWithValue("$barcode", line.Product.Barcode);
            insert.Parameters.AddWithValue("$name", line.Product.Name);
            insert.Parameters.AddWithValue("$unit", line.Product.Unit.ToString());
            insert.Parameters.AddWithValue("$unitPrice", Money(line.Product.Price));
            insert.Parameters.AddWithValue("$quantity", Money(line.Quantity));
            insert.Parameters.AddWithValue("$taxRate", Money(line.Product.TaxRate));
            insert.Parameters.AddWithValue("$lineTotal", Money(line.LineTotal));
            insert.Parameters.AddWithValue("$unitCost", Money(costs.GetValueOrDefault(line.Product.Id)));
            insert.ExecuteNonQuery();

            // Selling takes the goods off the shelf, inside this same transaction — a sale
            // that committed without moving stock would leave the count permanently wrong.
            if (line.Product.Id > 0)
                InventoryRepository.Move(line.Product.Id, -line.Quantity, StockReason.Sale,
                    reference: $"Sale #{invoiceNumber}",
                    unitCost: costs.GetValueOrDefault(line.Product.Id), connection: connection);
        }

        ActivityRepository.Record("completed a sale", "Sale", invoiceNumber,
            newValue: $"{total:0.00} DH",
            detail: $"completed sale #{invoiceNumber} for {total:0.00} DH", connection: connection);

        transaction.Commit();

        // The sale is on this machine's books. If this machine is a till on a shop network,
        // the books that matter are on the other one, so it goes into the queue in the same
        // breath — queueing anywhere else would mean a path that takes money without
        // recording that it owes the server a copy.
        //
        // Never for a sale that arrived from a till: that is the server writing what it was
        // handed, and queueing it would send it straight back where it came from.
        if (origin is null) Handover(lines, grossBeforeDiscount, discountKind, discountValue,
                                     discountAmount, subtotal, tax, total, method, amountTendered,
                                     soldAt, workerId, workerName, tillReference);

        return invoiceNumber;
    }

    /// <summary>
    /// Copies a sale into the outbox for the server. Does nothing at all on a shop with one
    /// computer, which is every shop until somebody puts a second till on the counter.
    /// </summary>
    private static void Handover(
        IReadOnlyList<SaleItem> lines, decimal gross, DiscountKind discountKind,
        decimal discountValue, decimal discountAmount, decimal subtotal, decimal tax,
        decimal total, PaymentMethod method, decimal tendered, DateTime soldAt,
        int? workerId, string workerName, string reference)
    {
        if (!ShopLink.IsConfigured) return;

        ShopLink.Queue(new Link.SaleUpload(
            reference, soldAt, workerId, workerName, method.ToString(), tendered,
            gross, discountKind.ToString(), discountValue, discountAmount,
            subtotal, tax, total,
            lines.Select(l => new Link.SaleLineDto(
                l.Product.Id, l.Product.Barcode, l.Product.Name, l.Quantity,
                l.Product.Price, l.Product.TaxRate, l.Product.Unit.ToString())).ToList()));
    }

    /// <summary>Current cost price per product id, for the lines about to be written.</summary>
    private static Dictionary<int, decimal> ReadCosts(
        Microsoft.Data.Sqlite.SqliteConnection connection, IReadOnlyList<SaleItem> lines)
    {
        var costs = new Dictionary<int, decimal>();
        var ids = lines.Select(l => l.Product.Id).Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return costs;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, cost FROM products WHERE id IN ({string.Join(",", ids)});";
        using var reader = command.ExecuteReader();
        while (reader.Read()) costs[reader.GetInt32(0)] = Parse(reader.GetString(1));
        return costs;
    }

    /// <summary>
    /// Reads a completed sale back for reprinting. Read-only by design: a reprint must never
    /// create a sale, touch stock, or move revenue.
    /// </summary>
    public static Receipt? FindByInvoiceNumber(int invoiceNumber)
    {
        using var connection = Database.Open();

        using var head = connection.CreateCommand();
        head.CommandText = @"
            SELECT id, invoice_number, sold_at, subtotal, tax, total, payment_method,
                   amount_tendered, change_given, gross_before_discount,
                   discount_kind, discount_value, discount_amount
            FROM sales WHERE invoice_number = $invoice AND is_voided = 0;";
        head.Parameters.AddWithValue("$invoice", invoiceNumber);

        long saleId;
        int number;
        DateTime soldAt;
        decimal subtotal, tax, total, tendered, change, gross, discountValue, discountAmount;
        PaymentMethod method;
        DiscountKind kind;

        using (var reader = head.ExecuteReader())
        {
            if (!reader.Read()) return null;
            saleId = reader.GetInt64(0);
            number = reader.GetInt32(1);
            soldAt = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            subtotal = Parse(reader.GetString(3));
            tax = Parse(reader.GetString(4));
            total = Parse(reader.GetString(5));
            method = Enum.TryParse<PaymentMethod>(reader.GetString(6), out var m) ? m : PaymentMethod.Cash;
            tendered = Parse(reader.GetString(7));
            change = Parse(reader.GetString(8));
            gross = Parse(reader.GetString(9));
            kind = Enum.TryParse<DiscountKind>(reader.GetString(10), out var k) ? k : DiscountKind.None;
            discountValue = Parse(reader.GetString(11));
            discountAmount = Parse(reader.GetString(12));
        }

        var lines = new List<ReceiptLine>();
        using var lineCommand = connection.CreateCommand();
        lineCommand.CommandText = @"
            SELECT name, quantity, unit, unit_price, line_total
            FROM sale_lines WHERE sale_id = $saleId ORDER BY id;";
        lineCommand.Parameters.AddWithValue("$saleId", saleId);
        using (var reader = lineCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                lines.Add(new ReceiptLine
                {
                    Name = reader.GetString(0),
                    Quantity = Parse(reader.GetString(1)),
                    Unit = reader.GetString(2) == nameof(Unit.Kg) ? Unit.Kg : Unit.Each,
                    UnitPrice = Parse(reader.GetString(3)),
                    LineTotal = Parse(reader.GetString(4)),
                });
            }
        }

        return new Receipt
        {
            InvoiceNumber = number,
            SoldAt = soldAt,
            Lines = lines,
            GrossBeforeDiscount = gross,
            DiscountKind = kind,
            DiscountValue = discountValue,
            DiscountAmount = discountAmount,
            Subtotal = subtotal,
            Tax = tax,
            Total = total,
            PaymentMethod = method,
            AmountTendered = tendered,
            ChangeGiven = change,
        };
    }

    /// <summary>
    /// Sales history for the Tickets page, newest first. An all-digit query matches an exact
    /// receipt number; anything else is ignored, since there is nothing else to search on yet.
    /// </summary>
    public static List<SaleSummary> ListSales(string? query = null, int limit = 200)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var hasNumber = int.TryParse((query ?? string.Empty).Trim(), out var number);
        command.CommandText = @"
            SELECT s.invoice_number, s.sold_at, s.total, s.discount_amount, s.payment_method,
                   (SELECT COUNT(*) FROM sale_lines l WHERE l.sale_id = s.id)
            FROM sales s
            WHERE s.is_voided = 0" + (hasNumber ? " AND s.invoice_number = $number" : string.Empty) + @"
            ORDER BY s.id DESC LIMIT $limit;";
        if (hasNumber) command.Parameters.AddWithValue("$number", number);
        command.Parameters.AddWithValue("$limit", limit);

        var sales = new List<SaleSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sales.Add(new SaleSummary
            {
                InvoiceNumber = reader.GetInt32(0),
                SoldAt = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                Total = Parse(reader.GetString(2)),
                DiscountAmount = Parse(reader.GetString(3)),
                PaymentMethod = Enum.TryParse<PaymentMethod>(reader.GetString(4), out var m) ? m : PaymentMethod.Cash,
                LineCount = reader.GetInt32(5),
            });
        }
        return sales;
    }

    /// <summary>Takings for a day — shown as a header figure on the Tickets page.</summary>
    public static (int Count, decimal Total) DayTotals(DateTime day)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*), COALESCE(SUM(CAST(total AS REAL)), 0)
            FROM sales WHERE is_voided = 0 AND sold_at >= $from AND sold_at < $to;";
        command.Parameters.AddWithValue("$from", day.Date.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", day.Date.AddDays(1).ToString("O", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return (0, 0m);
        return (reader.GetInt32(0), (decimal)reader.GetDouble(1));
    }

    /// <summary>
    /// Permanently deletes every sale and its lines, and returns how many went.
    ///
    /// This is a real erase, not a void: the revenue history goes with it, and because
    /// invoice numbers are allocated as MAX(invoice_number)+1 the sequence restarts at 1.
    /// Intended for clearing test data before the shop goes live.
    /// </summary>
    public static int DeleteAllSales()
    {
        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM sales;";
        var deleted = Convert.ToInt32(count.ExecuteScalar());

        using var wipe = connection.CreateCommand();
        wipe.CommandText = "DELETE FROM sale_lines; DELETE FROM sales;";
        wipe.ExecuteNonQuery();

        transaction.Commit();
        return deleted;
    }

    /// <summary>Most recent invoice numbers, so the cashier need not remember the last sale.</summary>
    public static List<int> RecentInvoiceNumbers(int count = 8)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT invoice_number FROM sales WHERE is_voided = 0 ORDER BY id DESC LIMIT $count;";
        command.Parameters.AddWithValue("$count", count);
        var numbers = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) numbers.Add(reader.GetInt32(0));
        return numbers;
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static decimal Parse(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
