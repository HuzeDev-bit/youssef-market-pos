using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MarketPos.Data;

/// <summary>
/// The small conversions every repository needs. Money and dates are stored as text
/// (see <see cref="Schema"/>); these are the only places that knowledge is encoded, so a
/// change of storage format is one file rather than twenty.
/// </summary>
internal static class Db
{
    public static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static decimal ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    public static string Stamp(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static DateTime ParseStamp(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d
            : DateTime.MinValue;

    // Column readers that tolerate NULL, so a row added by an older version does not throw.
    public static string Str(this SqliteDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
    public static decimal Dec(this SqliteDataReader r, int i) => r.IsDBNull(i) ? 0m : ParseMoney(r.GetString(i));
    public static int Int(this SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);
    public static bool Bool(this SqliteDataReader r, int i) => !r.IsDBNull(i) && r.GetInt32(i) != 0;
    public static DateTime Date(this SqliteDataReader r, int i) => r.IsDBNull(i) ? DateTime.MinValue : ParseStamp(r.GetString(i));
    public static DateTime? DateOrNull(this SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : ParseStamp(r.GetString(i));

    /// <summary>Adds a parameter, mapping null to DBNull so callers need not.</summary>
    public static SqliteCommand With(this SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static SqliteCommand WithMoney(this SqliteCommand command, string name, decimal value) =>
        command.With(name, Money(value));

    public static SqliteCommand WithDate(this SqliteCommand command, string name, DateTime value) =>
        command.With(name, Stamp(value));

    public static SqliteCommand WithDate(this SqliteCommand command, string name, DateTime? value) =>
        command.With(name, value.HasValue ? Stamp(value.Value) : null);

    /// <summary>
    /// SUM over a money column. The cast to REAL is safe here and only here: it is a
    /// read-only aggregate for a report, never a value that gets written back.
    /// </summary>
    public const string SumMoney = "COALESCE(SUM(CAST({0} AS REAL)), 0)";

    public static string Sum(string column) => string.Format(SumMoney, column);
}
