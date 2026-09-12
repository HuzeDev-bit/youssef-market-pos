using MarketPos.Models;
using MarketPos.Services;
using Microsoft.Data.Sqlite;

namespace MarketPos.Data;

/// <summary>
/// The audit trail. Every write that changes money or stock records one line here, including
/// the value on both sides of the change, so "the till is short" can be traced to a person
/// and a moment rather than argued about.
///
/// Recording never throws: an audit failure must not roll back the sale it was describing.
/// </summary>
public static class ActivityRepository
{
    /// <summary>
    /// Separates a stored sentence's pattern from the shop's own words inside it.
    ///
    /// A unit separator, which is a control character no product name, supplier or reason can
    /// contain — so splitting on it can never cut a sentence in the wrong place.
    /// </summary>
    public const char Apart = '';

    /// <summary>
    /// Builds a detail that can still be read back in another language.
    ///
    /// The log used to store finished English sentences — "deactivated Cahier de notes" — and
    /// that is a sentence nothing can ever translate: the words and the shop's own data are
    /// baked together, and there is no way to tell which is which afterwards. So the pattern
    /// and its values are kept apart here and only joined when they are shown, by which time
    /// the pattern has been looked up in whatever language the shop is running in.
    ///
    /// Storing the translation instead would be worse than leaving it English: the log would
    /// then be written in whatever language happened to be selected on the day, and an audit
    /// trail nobody can read end to end is not an audit trail.
    /// </summary>
    /// <param name="pattern">
    /// English, with {0} where each value goes. It is the key the translation is found by, so
    /// it must not be built from anything that varies.
    /// </param>
    public static string Say(string pattern, params object?[] values) =>
        values.Length == 0
            ? pattern
            : pattern + Apart + string.Join(Apart, values.Select(v => v?.ToString() ?? string.Empty));

    /// <summary>
    /// Writes one entry. Pass an open connection when the caller is inside a transaction, so
    /// the log lands or rolls back with the thing it describes.
    /// </summary>
    public static void Record(
        string action,
        string entity = "",
        object? entityId = null,
        string oldValue = "",
        string newValue = "",
        string detail = "",
        SqliteConnection? connection = null)
    {
        try
        {
            var own = connection is null;
            var db = connection ?? Database.Open();
            try
            {
                using var command = db.CreateCommand();
                command.CommandText = """
                    INSERT INTO activity_log
                        (happened_at, worker_id, worker_name, action, entity, entity_id,
                         old_value, new_value, detail)
                    VALUES ($at, $workerId, $workerName, $action, $entity, $entityId,
                            $old, $new, $detail);
                    """;
                command.WithDate("$at", DateTime.Now)
                       .With("$workerId", Session.CurrentId)
                       .With("$workerName", Session.CurrentName)
                       .With("$action", action)
                       .With("$entity", entity)
                       .With("$entityId", entityId?.ToString() ?? string.Empty)
                       .With("$old", oldValue)
                       .With("$new", newValue)
                       .With("$detail", string.IsNullOrWhiteSpace(detail) ? action : detail);
                command.ExecuteNonQuery();
            }
            finally
            {
                if (own) db.Dispose();
            }
        }
        catch
        {
            // A broken audit line must never take a sale or a stock movement down with it.
        }
    }

    public static List<ActivityEntry> List(DateRange? range = null, string? search = null, int limit = 300)
    {
        Session.Require(Permission.SeeActivityLog);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (range is { } r)
        {
            where.Add("happened_at >= $from AND happened_at < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(worker_name LIKE $q OR action LIKE $q OR entity LIKE $q OR detail LIKE $q)");
            command.With("$q", $"%{search.Trim()}%");
        }

        command.CommandText = $"""
            SELECT id, happened_at, worker_name, action, entity, entity_id, old_value, new_value, detail
            FROM activity_log
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var entries = new List<ActivityEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new ActivityEntry
            {
                Id = reader.Int(0),
                HappenedAt = reader.Date(1),
                WorkerName = reader.Str(2),
                Action = reader.Str(3),
                Entity = reader.Str(4),
                EntityId = reader.Str(5),
                OldValue = reader.Str(6),
                NewValue = reader.Str(7),
                Detail = reader.Str(8),
            });
        }
        return entries;
    }
}
