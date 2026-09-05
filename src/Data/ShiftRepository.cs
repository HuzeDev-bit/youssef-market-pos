using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>
/// Cashier shifts and the cash drawer.
///
/// Cash sales are never copied into cash_movements — they are read from the sales table, so
/// the drawer and the sales history cannot drift apart. cash_movements holds only what a
/// person put in or took out by hand: a float, a payout, petty cash for bread flour.
///
/// Expected cash = opening + cash sales + cash in − cash out. The difference against what was
/// actually counted is the number the owner wants, and it is never rounded away.
/// </summary>
public static class ShiftRepository
{
    public static Shift? OpenShift(int? workerId = null)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT s.id, s.worker_id, w.name, s.started_at, s.ended_at, s.opening_cash,
                   s.closing_cash, s.note
            FROM shifts s JOIN workers w ON w.id = s.worker_id
            WHERE s.ended_at IS NULL {(workerId is null ? string.Empty : "AND s.worker_id = $workerId")}
            ORDER BY s.id DESC LIMIT 1;
            """;
        if (workerId is { } id) command.With("$workerId", id);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var shift = ReadShift(reader);
        Fill(shift);
        return shift;
    }

    public static int Start(int workerId, string workerName, decimal openingCash, string note = "")
    {
        if (OpenShift(workerId) is not null)
            throw new InvalidOperationException($"{workerName} already has a shift open.");

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO shifts (worker_id, started_at, opening_cash, note)
            VALUES ($workerId, $at, $opening, $note);
            SELECT last_insert_rowid();
            """;
        command.With("$workerId", workerId).WithDate("$at", DateTime.Now)
               .WithMoney("$opening", openingCash).With("$note", note);
        var id = Convert.ToInt32(command.ExecuteScalar());

        ActivityRepository.Record("started a shift", "Shift", id, newValue: $"{openingCash:0.00} DH",
            detail: $"started a shift with {openingCash:0.00} DH in the drawer");
        return id;
    }

    /// <summary>Closes the shift against a counted figure and returns the difference.</summary>
    public static decimal End(int shiftId, decimal countedCash, string note = "")
    {
        var shift = Find(shiftId) ?? throw new InvalidOperationException("That shift no longer exists.");
        var difference = countedCash - shift.ExpectedCash;

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE shifts SET ended_at = $at, closing_cash = $closing,
                   note = CASE WHEN $note = '' THEN note ELSE note || ' ' || $note END
            WHERE id = $id;
            """;
        command.WithDate("$at", DateTime.Now).WithMoney("$closing", countedCash)
               .With("$note", note).With("$id", shiftId);
        command.ExecuteNonQuery();

        ActivityRepository.Record("ended a shift", "Shift", shiftId,
            oldValue: $"{shift.ExpectedCash:0.00} DH expected",
            newValue: $"{countedCash:0.00} DH counted",
            detail: $"ended a shift {(difference == 0m ? "exactly on" : difference > 0m ? "over" : "short")}");
        return difference;
    }

    public static Shift? Find(int id)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.worker_id, w.name, s.started_at, s.ended_at, s.opening_cash,
                   s.closing_cash, s.note
            FROM shifts s JOIN workers w ON w.id = s.worker_id WHERE s.id = $id;
            """;
        command.With("$id", id);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var shift = ReadShift(reader);
        Fill(shift);
        return shift;
    }

    public static List<Shift> List(DateRange? range = null, int? workerId = null, int limit = 200)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (range is { } r)
        {
            where.Add("s.started_at >= $from AND s.started_at < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (workerId is { } wid)
        {
            where.Add("s.worker_id = $wid");
            command.With("$wid", wid);
        }

        command.CommandText = $"""
            SELECT s.id, s.worker_id, w.name, s.started_at, s.ended_at, s.opening_cash,
                   s.closing_cash, s.note
            FROM shifts s JOIN workers w ON w.id = s.worker_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY s.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var shifts = new List<Shift>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) shifts.Add(ReadShift(reader));
        }
        foreach (var shift in shifts) Fill(shift);
        return shifts;
    }

    private static Shift ReadShift(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.Int(0),
        WorkerId = reader.Int(1),
        WorkerName = reader.Str(2),
        StartedAt = reader.Date(3),
        EndedAt = reader.DateOrNull(4),
        OpeningCash = reader.Dec(5),
        ClosingCash = reader.IsDBNull(6) ? null : reader.Dec(6),
        Note = reader.Str(7),
    };

    /// <summary>
    /// Reads the shift's takings straight from the sales and cash tables. Nothing is cached
    /// on the shift row, so a sale that arrives late still lands in the right shift.
    /// </summary>
    private static void Fill(Shift shift)
    {
        using var connection = Database.Open();

        using (var sales = connection.CreateCommand())
        {
            sales.CommandText = $"""
                SELECT COUNT(*), {Db.Sum("total")},
                       COALESCE(SUM(CASE WHEN payment_method = 'Cash' THEN CAST(total AS REAL) ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN payment_method = 'Card' THEN CAST(total AS REAL) ELSE 0 END), 0)
                FROM sales
                WHERE is_voided = 0 AND status <> 'Cancelled' AND shift_id = $shiftId;
                """;
            sales.With("$shiftId", shift.Id);
            using var reader = sales.ExecuteReader();
            if (reader.Read())
            {
                shift.SaleCount = reader.GetInt32(0);
                shift.Sales = (decimal)reader.GetDouble(1);
                shift.CashSales = (decimal)reader.GetDouble(2);
                shift.CardSales = (decimal)reader.GetDouble(3);
            }
        }

        using (var cash = connection.CreateCommand())
        {
            cash.CommandText = """
                SELECT COALESCE(SUM(CASE WHEN CAST(amount AS REAL) > 0 THEN CAST(amount AS REAL) ELSE 0 END), 0),
                       COALESCE(SUM(CASE WHEN CAST(amount AS REAL) < 0 THEN -CAST(amount AS REAL) ELSE 0 END), 0)
                FROM cash_movements WHERE shift_id = $shiftId;
                """;
            cash.With("$shiftId", shift.Id);
            using var reader = cash.ExecuteReader();
            if (reader.Read())
            {
                shift.CashIn = (decimal)reader.GetDouble(0);
                shift.CashOut = (decimal)reader.GetDouble(1);
            }
        }
    }

    // ---------------------------- Cash drawer ----------------------------

    /// <summary>Money in or out of the drawer by hand. Negative takes money out.</summary>
    public static void RecordCash(decimal amount, string reason, string note = "", int? shiftId = null)
    {
        Session.Require(Permission.ManageCash);
        if (amount == 0m) throw new ArgumentException("Enter an amount.", nameof(amount));

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cash_movements (shift_id, moved_at, amount, reason, note, worker_id)
            VALUES ($shiftId, $at, $amount, $reason, $note, $workerId);
            """;
        command.With("$shiftId", shiftId ?? OpenShift()?.Id)
               .WithDate("$at", DateTime.Now)
               .WithMoney("$amount", amount)
               .With("$reason", reason)
               .With("$note", note)
               .With("$workerId", Session.CurrentId);
        command.ExecuteNonQuery();

        ActivityRepository.Record(amount > 0 ? "put cash in the drawer" : "took cash out of the drawer",
            "Cash", null, newValue: $"{Math.Abs(amount):0.00} DH",
            detail: $"{(amount > 0 ? "added" : "removed")} {Math.Abs(amount):0.00} DH ({reason})");
    }

    public static List<CashMovement> ListCash(DateRange? range = null, int limit = 200)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var filter = string.Empty;
        if (range is { } r)
        {
            filter = "WHERE m.moved_at >= $from AND m.moved_at < $to";
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }

        command.CommandText = $"""
            SELECT m.id, m.shift_id, m.moved_at, m.amount, m.reason, m.note, COALESCE(w.name, '')
            FROM cash_movements m LEFT JOIN workers w ON w.id = m.worker_id
            {filter}
            ORDER BY m.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var movements = new List<CashMovement>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            movements.Add(new CashMovement
            {
                Id = reader.Int(0),
                ShiftId = reader.IsDBNull(1) ? null : reader.Int(1),
                MovedAt = reader.Date(2),
                Amount = reader.Dec(3),
                Reason = reader.Str(4),
                Note = reader.Str(5),
                WorkerName = reader.Str(6),
            });
        }
        return movements;
    }

    /// <summary>
    /// The drawer position for a whole period rather than one shift — what the Cash page shows
    /// when the owner picks "This week".
    /// </summary>
    public static (decimal Opening, decimal CashSales, decimal In, decimal Out, decimal Expected,
                   decimal? Counted, decimal? Difference) Position(DateRange range)
    {
        var shifts = List(range);

        var opening = shifts.Sum(s => s.OpeningCash);
        var cashSales = shifts.Sum(s => s.CashSales);
        var moved = ListCash(range, limit: 10_000);
        var cashIn = moved.Where(m => m.Amount > 0).Sum(m => m.Amount);
        var cashOut = moved.Where(m => m.Amount < 0).Sum(m => -m.Amount);
        var expected = opening + cashSales + cashIn - cashOut;

        var closed = shifts.Where(s => s.ClosingCash.HasValue).ToList();
        decimal? counted = closed.Count > 0 ? closed.Sum(s => s.ClosingCash!.Value) : null;

        // A difference is only meaningful once every shift in the window has been counted.
        decimal? difference = counted.HasValue && closed.Count == shifts.Count
            ? counted.Value - expected
            : null;

        return (opening, cashSales, cashIn, cashOut, expected, counted, difference);
    }
}
