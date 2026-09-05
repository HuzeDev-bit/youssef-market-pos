using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>
/// Operating expenses — rent, power, water, repairs and the like.
///
/// Deliberately NOT here: stock bought from suppliers. That is inventory, and the money
/// leaving is a supplier payment; recording it as an expense as well would count the same
/// dirham twice. Salary payments are likewise stored once, in salary_payments, and read into
/// the expense picture by <see cref="Services.Finance"/> rather than copied into this table.
/// </summary>
public static class ExpenseRepository
{
    public static List<Expense> List(DateRange? range = null, int? categoryId = null,
                                     string? search = null, int limit = 400)
    {
        Session.Require(Permission.ManageExpenses);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string> { "e.is_void = 0" };
        if (range is { } r)
        {
            where.Add("e.spent_on >= $from AND e.spent_on < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (categoryId is { } cid)
        {
            where.Add("e.category_id = $cid");
            command.With("$cid", cid);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(e.name LIKE $q OR e.note LIKE $q)");
            command.With("$q", $"%{search.Trim()}%");
        }

        command.CommandText = $"""
            SELECT e.id, e.name, e.category_id, COALESCE(c.name, 'Other'), e.amount, e.spent_on,
                   e.method, e.note, e.receipt_path, e.recurring, e.is_void
            FROM expenses e
            LEFT JOIN expense_categories c ON c.id = e.category_id
            WHERE {string.Join(" AND ", where)}
            ORDER BY e.spent_on DESC, e.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var expenses = new List<Expense>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            expenses.Add(new Expense
            {
                Id = reader.Int(0),
                Name = reader.Str(1),
                CategoryId = reader.IsDBNull(2) ? null : reader.Int(2),
                Category = reader.Str(3),
                Amount = reader.Dec(4),
                SpentOn = reader.Date(5),
                Method = reader.Str(6),
                Note = reader.Str(7),
                ReceiptPath = reader.IsDBNull(8) ? null : reader.Str(8),
                Recurring = Enum.TryParse<Recurrence>(reader.Str(9), out var rec) ? rec : Recurrence.None,
                IsVoid = reader.Bool(10),
            });
        }
        return expenses;
    }

    public static int Create(Expense expense)
    {
        Session.Require(Permission.ManageExpenses);
        if (expense.Amount <= 0m)
            throw new ArgumentException("An expense must be greater than zero.", nameof(expense));

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO expenses (name, category_id, amount, spent_on, method, note,
                                  receipt_path, recurring, is_void, created_by, created_at)
            VALUES ($name, $categoryId, $amount, $on, $method, $note,
                    $receipt, $recurring, 0, $by, $now);
            SELECT last_insert_rowid();
            """;
        command.With("$name", expense.Name)
               .With("$categoryId", expense.CategoryId)
               .WithMoney("$amount", expense.Amount)
               .WithDate("$on", expense.SpentOn)
               .With("$method", expense.Method)
               .With("$note", expense.Note)
               .With("$receipt", expense.ReceiptPath)
               .With("$recurring", expense.Recurring.ToString())
               .With("$by", Session.CurrentId)
               .WithDate("$now", DateTime.Now);
        var id = Convert.ToInt32(command.ExecuteScalar());

        ActivityRepository.Record("recorded an expense", "Expense", id,
            newValue: $"{expense.Amount:0.00} DH",
            detail: $"recorded a {expense.Amount:0.00} DH expense for {expense.Name}");
        return id;
    }

    public static void Update(Expense expense)
    {
        Session.Require(Permission.ManageExpenses);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE expenses SET name = $name, category_id = $categoryId, amount = $amount,
                   spent_on = $on, method = $method, note = $note, receipt_path = $receipt,
                   recurring = $recurring
            WHERE id = $id;
            """;
        command.With("$name", expense.Name)
               .With("$categoryId", expense.CategoryId)
               .WithMoney("$amount", expense.Amount)
               .WithDate("$on", expense.SpentOn)
               .With("$method", expense.Method)
               .With("$note", expense.Note)
               .With("$receipt", expense.ReceiptPath)
               .With("$recurring", expense.Recurring.ToString())
               .With("$id", expense.Id);
        command.ExecuteNonQuery();

        ActivityRepository.Record("edited an expense", "Expense", expense.Id,
            newValue: $"{expense.Amount:0.00} DH", detail: $"edited the expense {expense.Name}");
    }

    /// <summary>Voids rather than deletes — a spent dirham that disappears is how books stop balancing.</summary>
    public static void Void(int id, string name, string reason)
    {
        Session.Require(Permission.ManageExpenses);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE expenses SET is_void = 1, note = note || $suffix WHERE id = $id;";
        command.With("$suffix", $" [voided: {reason}]").With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record("voided an expense", "Expense", id, newValue: reason,
            detail: $"voided the expense {name}");
    }

    /// <summary>Totals per category for the period — feeds the Money Spent breakdown.</summary>
    public static List<(string Category, decimal Amount)> ByCategory(DateRange range)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COALESCE(c.name, 'Other'), {Db.Sum("e.amount")}
            FROM expenses e
            LEFT JOIN expense_categories c ON c.id = e.category_id
            WHERE e.is_void = 0 AND e.spent_on >= $from AND e.spent_on < $to
            GROUP BY COALESCE(c.name, 'Other')
            ORDER BY 2 DESC;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To);

        var rows = new List<(string, decimal)>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add((reader.GetString(0), (decimal)reader.GetDouble(1)));
        return rows;
    }

    public static decimal Total(DateRange range)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Db.Sum("amount")} FROM expenses
            WHERE is_void = 0 AND spent_on >= $from AND spent_on < $to;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To);
        return (decimal)Convert.ToDouble(command.ExecuteScalar());
    }

    // ---------------------------- Categories ----------------------------

    public static List<(int Id, string Name)> Categories()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM expense_categories WHERE is_active = 1 ORDER BY name;";

        var rows = new List<(int, string)>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add((reader.GetInt32(0), reader.GetString(1)));
        return rows;
    }

    public static int AddCategory(string name)
    {
        Session.Require(Permission.ManageExpenses);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO expense_categories (name) VALUES ($name);
            SELECT id FROM expense_categories WHERE name = $name;
            """;
        command.With("$name", name.Trim());
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Copies last period's recurring expenses into this one. Called with the dates the owner
    /// confirms, so a rent line is never created behind their back — the owner presses the
    /// button, the system fills the form.
    /// </summary>
    public static List<Expense> DueRecurring(DateTime forMonth)
    {
        var start = new DateTime(forMonth.Year, forMonth.Month, 1);
        var previous = start.AddMonths(-1);

        var lastMonth = List(DateRange.Custom(previous, start.AddDays(-1)))
            .Where(e => e.Recurring != Recurrence.None)
            .ToList();

        var thisMonth = List(DateRange.Custom(start, start.AddMonths(1).AddDays(-1)))
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return lastMonth.Where(e => !thisMonth.Contains(e.Name)).ToList();
    }
}
