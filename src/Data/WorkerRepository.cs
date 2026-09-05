using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Data;

/// <summary>
/// Staff and pay.
///
/// Salary payments are append-only. Paying 2 000 of a 3 000 monthly salary writes a row for
/// the 2 000; the remaining 1 000 is derived, never stored, so a later payment cannot quietly
/// overwrite the first one.
/// </summary>
public static class WorkerRepository
{
    public static List<Worker> List(bool includeInactive = false)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, name, phone, email, role, started_on, salary, salary_period,
                   is_active, note, pin_hash
            FROM workers
            {(includeInactive ? string.Empty : "WHERE is_active = 1")}
            ORDER BY is_active DESC, name;
            """;

        var workers = new List<Worker>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            workers.Add(new Worker
            {
                Id = reader.Int(0),
                Name = reader.Str(1),
                Phone = reader.Str(2),
                Email = reader.Str(3),
                Role = Enum.TryParse<WorkerRole>(reader.Str(4), out var r) ? r : WorkerRole.Cashier,
                StartedOn = reader.Date(5),
                Salary = reader.Dec(6),
                SalaryPeriod = Enum.TryParse<SalaryPeriod>(reader.Str(7), out var p) ? p : SalaryPeriod.Monthly,
                IsActive = reader.Bool(8),
                Note = reader.Str(9),
                HasPin = reader.Str(10).Length > 0,
            });
        }
        return workers;
    }

    public static Worker? Find(int id) => List(includeInactive: true).FirstOrDefault(w => w.Id == id);

    /// <summary>
    /// Staff as a till needs to receive them, hashes included, so a cashier can sign in on a
    /// counter with the back office switched off. Only people who have been given a password:
    /// somebody with no way to sign in is nobody a till needs to know about.
    ///
    /// Deliberately its own method rather than a flag on <see cref="List"/>: a password hash
    /// should have to be asked for by name.
    /// </summary>
    public static List<(int Id, string Name, string Role, string Hash, string Salt, bool IsActive)> ForSync()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, role, pin_hash, pin_salt, is_active
            FROM workers WHERE pin_hash <> '' ORDER BY id;
            """;

        var rows = new List<(int, string, string, string, string, bool)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add((reader.Int(0), reader.Str(1), reader.Str(2),
                      reader.Str(3), reader.Str(4), reader.Bool(5)));
        return rows;
    }

    /// <summary>
    /// Writes the staff a server sent into this till's own database, so the sign-in list and
    /// the password check both work with nothing plugged in. Ids are the server's, exactly as
    /// with the catalogue — a sale names the person who rang it up.
    /// </summary>
    public static int ReplaceFromServer(IReadOnlyList<Link.StaffMember> staff)
    {
        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var person in staff)
        {
            using var upsert = connection.CreateCommand();
            upsert.CommandText = """
                INSERT INTO workers(id, name, role, started_on, salary, salary_period,
                                    is_active, pin_hash, pin_salt, created_at)
                VALUES($id, $name, $role, $now, '0', 'Monthly', $active, $hash, $salt, $now)
                ON CONFLICT(id) DO UPDATE SET
                    name      = excluded.name,
                    role      = excluded.role,
                    is_active = excluded.is_active,
                    pin_hash  = excluded.pin_hash,
                    pin_salt  = excluded.pin_salt;
                """;
            upsert.With("$id", person.Id)
                  .With("$name", person.Name)
                  .With("$role", person.Role)
                  .With("$active", person.IsActive ? 1 : 0)
                  .With("$hash", person.PinHash)
                  .With("$salt", person.PinSalt)
                  .WithDate("$now", DateTime.Now);
            upsert.ExecuteNonQuery();
        }

        // Somebody who has left, or had their password taken away, can no longer sign in here.
        // Deactivated rather than deleted: their name is on sales this till has already taken.
        using var retire = connection.CreateCommand();
        var ids = staff.Select(p => p.Id.ToString()).ToList();
        retire.CommandText = ids.Count == 0
            ? "UPDATE workers SET pin_hash = '', pin_salt = '';"
            : $"UPDATE workers SET pin_hash = '', pin_salt = '' WHERE id NOT IN ({string.Join(",", ids)});";
        retire.ExecuteNonQuery();

        transaction.Commit();
        return staff.Count;
    }

    public static int Create(Worker worker)
    {
        Session.Require(Permission.ManageWorkers);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workers (name, phone, email, role, started_on, salary, salary_period,
                                 is_active, note, created_at)
            VALUES ($name, $phone, $email, $role, $started, $salary, $period, 1, $note, $now);
            SELECT last_insert_rowid();
            """;
        Bind(command, worker);
        command.WithDate("$now", DateTime.Now);
        var id = Convert.ToInt32(command.ExecuteScalar());

        ActivityRepository.Record("added worker", "Worker", id, newValue: worker.Name,
                                  detail: $"added {worker.RoleLabel.ToLowerInvariant()} {worker.Name}");
        return id;
    }

    public static void Update(Worker worker)
    {
        Session.Require(Permission.ManageWorkers);
        var before = Find(worker.Id);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE workers SET name = $name, phone = $phone, email = $email, role = $role,
                   started_on = $started, salary = $salary, salary_period = $period, note = $note
            WHERE id = $id;
            """;
        Bind(command, worker);
        command.With("$id", worker.Id);
        command.ExecuteNonQuery();

        if (before is not null && before.Salary != worker.Salary)
            ActivityRepository.Record("changed salary", "Worker", worker.Id,
                oldValue: $"{before.Salary:0.00} DH", newValue: $"{worker.Salary:0.00} DH",
                detail: $"changed {worker.Name}'s salary");
        else
            ActivityRepository.Record("edited worker", "Worker", worker.Id, newValue: worker.Name,
                detail: $"edited worker {worker.Name}");
    }

    public static void SetActive(int id, string name, bool active)
    {
        Session.Require(Permission.ManageWorkers);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE workers SET is_active = $active WHERE id = $id;";
        command.With("$active", active ? 1 : 0).With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record(active ? "reactivated worker" : "deactivated worker",
            "Worker", id, newValue: name, detail: $"{(active ? "reactivated" : "deactivated")} {name}");
    }

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, Worker w) =>
        command.With("$name", w.Name).With("$phone", w.Phone).With("$email", w.Email)
               .With("$role", w.Role.ToString()).WithDate("$started", w.StartedOn)
               .WithMoney("$salary", w.Salary).With("$period", w.SalaryPeriod.ToString())
               .With("$note", w.Note);

    // ------------------------------- Sign-in -------------------------------

    /// <summary>Sets a worker's till PIN. Hashed the same way as the admin password.</summary>
    public static void SetPin(int id, string pin)
    {
        Session.Require(Permission.ManageWorkers);
        var (hash, salt) = PasswordHash.Create(pin);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE workers SET pin_hash = $hash, pin_salt = $salt WHERE id = $id;";
        command.With("$hash", hash).With("$salt", salt).With("$id", id);
        command.ExecuteNonQuery();

        ActivityRepository.Record("set a till PIN", "Worker", id, detail: "set a till PIN");
    }

    /// <summary>
    /// True once anybody has a password set. Until then the till does not ask for one — a shop
    /// whose staff list has not been filled in must still be able to work, and a gate nobody
    /// can pass is worse than no gate.
    /// </summary>
    public static bool AnyPasswordSet()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM workers WHERE is_active = 1 AND pin_hash <> '';";
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    /// <summary>Checks one named worker's password. Returns them when it matches.</summary>
    public static Worker? SignIn(int workerId, string password)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pin_hash, pin_salt FROM workers WHERE id = $id AND is_active = 1;";
        command.With("$id", workerId);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return PasswordHash.Verify(password, reader.Str(0), reader.Str(1)) ? Find(workerId) : null;
    }

    /// <summary>Finds the worker whose PIN this is, or null. Used for cashier sign-in at the till.</summary>
    public static Worker? SignIn(string pin)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, pin_hash, pin_salt FROM workers WHERE is_active = 1 AND pin_hash <> '';";

        var candidates = new List<(int Id, string Hash, string Salt)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) candidates.Add((reader.Int(0), reader.Str(1), reader.Str(2)));
        }

        // Every candidate is checked even after a match, so the time taken does not reveal
        // which position in the list a PIN belongs to.
        Worker? found = null;
        foreach (var (id, hash, salt) in candidates)
            if (PasswordHash.Verify(pin, hash, salt) && found is null)
                found = Find(id);

        return found;
    }

    // ------------------------------- Salaries ------------------------------

    /// <summary>
    /// What each worker is owed for the period and what they have been paid in it.
    /// Due is the contractual salary for the period; anything already paid inside the
    /// period is subtracted.
    /// </summary>
    public static List<SalaryLedger> Ledger(DateRange period)
    {
        Session.Require(Permission.SeeSalaries);

        var paidByWorker = new Dictionary<int, decimal>();
        using (var connection = Database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT worker_id, COALESCE(SUM(CAST(amount_paid AS REAL)), 0)
                FROM salary_payments WHERE paid_on >= $from AND paid_on < $to
                GROUP BY worker_id;
                """;
            command.WithDate("$from", period.From).WithDate("$to", period.To);
            using var reader = command.ExecuteReader();
            while (reader.Read()) paidByWorker[reader.Int(0)] = (decimal)reader.GetDouble(1);
        }

        return List().Select(w => new SalaryLedger
        {
            Worker = w,
            Due = DueFor(w, period),
            Paid = paidByWorker.GetValueOrDefault(w.Id),
        }).ToList();
    }

    /// <summary>
    /// Pro-rates the contractual salary onto the selected window, so "this week" on a monthly
    /// salary shows a week's worth rather than a whole month's.
    /// </summary>
    private static decimal DueFor(Worker worker, DateRange period)
    {
        var days = period.Days;
        return worker.SalaryPeriod switch
        {
            SalaryPeriod.Daily => worker.Salary * days,
            SalaryPeriod.Weekly => Math.Round(worker.Salary * days / 7m, 2),
            _ => Math.Round(worker.Salary * days / 30m, 2),
        };
    }

    public static void PaySalary(int workerId, string workerName, decimal amountDue, decimal amountPaid,
                                 DateRange period, DateTime paidOn, string method = "Cash", string note = "")
    {
        Session.Require(Permission.PaySalaries);
        if (amountPaid <= 0m) throw new ArgumentException("A payment must be greater than zero.", nameof(amountPaid));

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO salary_payments
                (worker_id, period_start, period_end, amount_due, amount_paid, paid_on,
                 method, note, created_by, created_at)
            VALUES ($workerId, $start, $end, $due, $paid, $on, $method, $note, $by, $now);
            """;
        command.With("$workerId", workerId)
               .WithDate("$start", period.From)
               .WithDate("$end", period.To.AddDays(-1))
               .WithMoney("$due", amountDue)
               .WithMoney("$paid", amountPaid)
               .WithDate("$on", paidOn)
               .With("$method", method)
               .With("$note", note)
               .With("$by", Session.CurrentId)
               .WithDate("$now", DateTime.Now);
        command.ExecuteNonQuery();

        ActivityRepository.Record("paid a salary", "Worker", workerId, newValue: $"{amountPaid:0.00} DH",
            detail: $"paid {workerName} {amountPaid:0.00} DH", connection: connection);
    }

    public static List<SalaryPayment> ListPayments(DateRange? range = null, int? workerId = null,
                                                   int limit = 300)
    {
        Session.Require(Permission.SeeSalaries);

        using var connection = Database.Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (range is { } r)
        {
            where.Add("sp.paid_on >= $from AND sp.paid_on < $to");
            command.WithDate("$from", r.From).WithDate("$to", r.To);
        }
        if (workerId is { } wid)
        {
            where.Add("sp.worker_id = $wid");
            command.With("$wid", wid);
        }

        command.CommandText = $"""
            SELECT sp.id, sp.worker_id, w.name, sp.period_start, sp.period_end,
                   sp.amount_due, sp.amount_paid, sp.paid_on, sp.method, sp.note
            FROM salary_payments sp
            JOIN workers w ON w.id = sp.worker_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
            ORDER BY sp.paid_on DESC, sp.id DESC LIMIT $limit;
            """;
        command.With("$limit", limit);

        var payments = new List<SalaryPayment>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            payments.Add(new SalaryPayment
            {
                Id = reader.Int(0),
                WorkerId = reader.Int(1),
                WorkerName = reader.Str(2),
                PeriodStart = reader.Date(3),
                PeriodEnd = reader.Date(4),
                AmountDue = reader.Dec(5),
                AmountPaid = reader.Dec(6),
                PaidOn = reader.Date(7),
                Method = reader.Str(8),
                Note = reader.Str(9),
            });
        }
        return payments;
    }

    /// <summary>Salary money that actually left the business in the period — an operating expense.</summary>
    public static decimal PaidIn(DateRange range)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Db.Sum("amount_paid")} FROM salary_payments
            WHERE paid_on >= $from AND paid_on < $to;
            """;
        command.WithDate("$from", range.From).WithDate("$to", range.To);
        return (decimal)Convert.ToDouble(command.ExecuteScalar());
    }
}
