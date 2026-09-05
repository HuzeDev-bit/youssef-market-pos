using Microsoft.Data.Sqlite;

namespace MarketPos.Data;

/// <summary>
/// The queue of sales this machine has taken and the server has not confirmed.
///
/// The whole point of the till keeping its own database is that the shop can keep selling when
/// the back-office machine is off, asleep or unplugged. What makes that safe rather than
/// merely optimistic is this table: every sale is written to it in the same breath as the sale
/// itself, and nothing is ever considered handed over until the server has said so by name.
///
/// Rows are marked, never deleted. "Did that sale ever reach the books" is a question a
/// shopkeeper will ask one day, and an empty table is not an answer.
/// </summary>
public static class OutboxRepository
{
    public sealed record Waiting(long Id, string Reference, string Payload, DateTime CreatedAt, int Attempts);

    /// <summary>
    /// Puts a sale in the queue. Silently does nothing if the reference is already there —
    /// the same sale being queued twice is a bug, but one that must not throw in the middle
    /// of taking money.
    /// </summary>
    public static void Queue(string reference, string payload)
    {
        if (string.IsNullOrWhiteSpace(reference)) return;

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO outbox(reference, payload, created_at)
            VALUES($ref, $payload, $at);
            """;
        command.With("$ref", reference)
               .With("$payload", payload)
               .WithDate("$at", DateTime.Now);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The oldest unsent sales first — a shop's books read in the order the shop traded, and
    /// a till that has been off for a day should not deliver its afternoon before its morning.
    /// </summary>
    public static List<Waiting> Pending(int limit = 50)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, reference, payload, created_at, attempts
            FROM outbox WHERE sent_at = '' ORDER BY id LIMIT $limit;
            """;
        command.With("$limit", limit);

        var rows = new List<Waiting>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new Waiting(reader.GetInt64(0), reader.Str(1), reader.Str(2),
                                 reader.Date(3), reader.Int(4)));
        return rows;
    }

    public static int PendingCount()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM outbox WHERE sent_at = '';";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Records that the server has the sale, and what it called it. The server's invoice
    /// number is kept because it is the number in the books — the till's own is only the
    /// number on the paper the customer walked out with.
    /// </summary>
    public static void MarkSent(string reference, int invoiceNumber)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE outbox SET sent_at = $at, invoice = $invoice, last_error = ''
            WHERE reference = $ref;
            """;
        command.WithDate("$at", DateTime.Now)
               .With("$invoice", invoiceNumber)
               .With("$ref", reference);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The server refused this one. Counted rather than dropped: a sale that cannot be
    /// delivered is something a person has to look at, and it stays in the queue until they do.
    /// </summary>
    public static void MarkFailed(string reference, string reason)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE outbox SET attempts = attempts + 1, last_error = $why WHERE reference = $ref;
            """;
        command.With("$why", reason.Length > 300 ? reason[..300] : reason)
               .With("$ref", reference);
        command.ExecuteNonQuery();
    }

    /// <summary>Sales the server keeps refusing, for the page that has to show somebody.</summary>
    public static List<Waiting> Stuck(int afterAttempts = 3) =>
        Pending(200).Where(w => w.Attempts >= afterAttempts).ToList();
}
