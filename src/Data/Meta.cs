namespace MarketPos.Data;

/// <summary>
/// A handful of facts about this database file — not about the shop.
///
/// Which catalogue the till last pulled belongs here rather than in the settings file: the
/// settings survive a database being restored from a backup, and a till that had just been
/// rolled back a week would go on insisting it was up to date.
/// </summary>
public static class Meta
{
    public static string Get(string key, string fallback = "")
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.With("$key", key);
        return command.ExecuteScalar() as string ?? fallback;
    }

    public static void Set(string key, string value)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO meta(key, value) VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.With("$key", key).With("$value", value);
        command.ExecuteNonQuery();
    }
}
