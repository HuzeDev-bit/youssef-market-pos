using Microsoft.Data.Sqlite;

namespace MarketPos.Data;

/// <summary>
/// The back-office schema: everything beyond the till itself — stock, suppliers, purchases,
/// staff, salaries, expenses, cash and the audit trail.
///
/// Money is TEXT everywhere, for the same reason the sales tables use it: SQLite's REAL is a
/// double and a double cannot hold 0.10 exactly, so totals drift by centimes over a year of
/// trading. Dates are ISO-8601 round-trip strings ("O"), which sort correctly as text.
///
/// Two accounting rules are baked into the shape of these tables, because getting them wrong
/// is how a shop ends up with two different answers to "how much did I make":
///
///   1. Buying stock is not an expense. A supplier purchase creates inventory (an asset).
///      The money leaves when a <c>supplier_payments</c> row is written, and the cost only
///      reaches profit as COGS when the item is actually sold. Recording a purchase as an
///      expense as well would count the same dirham twice.
///
///   2. Cost is snapshotted onto the sale line. <c>sale_lines.unit_cost</c> is copied from the
///      product at the moment of sale, so re-pricing a product next month cannot rewrite last
///      month's profit.
/// </summary>
internal static class Schema
{
    public static void Apply(SqliteConnection connection)
    {
        Create(connection);
        Extend(connection);
    }

    private static void Create(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            -- ============================ People ============================

            -- Staff and logins are the same record: a cashier who can sign in is a worker
            -- with a password. Splitting them would mean maintaining two lists of the
            -- same people and letting them disagree.
            CREATE TABLE IF NOT EXISTS workers (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                name          TEXT    NOT NULL,
                phone         TEXT    NOT NULL DEFAULT '',
                email         TEXT    NOT NULL DEFAULT '',
                role          TEXT    NOT NULL DEFAULT 'Cashier',   -- Owner|Manager|Cashier|StockWorker
                started_on    TEXT    NOT NULL,
                salary        TEXT    NOT NULL DEFAULT '0',
                salary_period TEXT    NOT NULL DEFAULT 'Monthly',   -- Monthly|Weekly|Daily
                is_active     INTEGER NOT NULL DEFAULT 1,
                pin_hash      TEXT    NOT NULL DEFAULT '',
                pin_salt      TEXT    NOT NULL DEFAULT '',
                note          TEXT    NOT NULL DEFAULT '',
                created_at    TEXT    NOT NULL
            );

            -- One row per salary payment, never an updated running total: paying 2000 of a
            -- 3000 salary must leave both the 2000 and the fact that 1000 is still owed.
            CREATE TABLE IF NOT EXISTS salary_payments (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                worker_id    INTEGER NOT NULL REFERENCES workers(id),
                period_start TEXT    NOT NULL,
                period_end   TEXT    NOT NULL,
                amount_due   TEXT    NOT NULL,
                amount_paid  TEXT    NOT NULL,
                paid_on      TEXT    NOT NULL,
                method       TEXT    NOT NULL DEFAULT 'Cash',
                note         TEXT    NOT NULL DEFAULT '',
                created_by   INTEGER          REFERENCES workers(id),
                created_at   TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_salary_worker ON salary_payments(worker_id);
            CREATE INDEX IF NOT EXISTS ix_salary_paid   ON salary_payments(paid_on);

            -- ========================== Suppliers ===========================

            CREATE TABLE IF NOT EXISTS suppliers (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                name       TEXT    NOT NULL,
                contact    TEXT    NOT NULL DEFAULT '',
                phone      TEXT    NOT NULL DEFAULT '',
                email      TEXT    NOT NULL DEFAULT '',
                address    TEXT    NOT NULL DEFAULT '',
                note       TEXT    NOT NULL DEFAULT '',
                is_active  INTEGER NOT NULL DEFAULT 1,
                created_at TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS purchases (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                supplier_id    INTEGER NOT NULL REFERENCES suppliers(id),
                invoice_number TEXT    NOT NULL DEFAULT '',
                purchased_on   TEXT    NOT NULL,
                due_on         TEXT,
                total          TEXT    NOT NULL DEFAULT '0',
                method         TEXT    NOT NULL DEFAULT 'Cash',
                note           TEXT    NOT NULL DEFAULT '',
                status         TEXT    NOT NULL DEFAULT 'Received',  -- Received|Cancelled
                received       INTEGER NOT NULL DEFAULT 1,           -- has stock been added?
                created_by     INTEGER          REFERENCES workers(id),
                created_at     TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_purchases_supplier ON purchases(supplier_id);
            CREATE INDEX IF NOT EXISTS ix_purchases_date     ON purchases(purchased_on);

            CREATE TABLE IF NOT EXISTS purchase_lines (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                purchase_id INTEGER NOT NULL REFERENCES purchases(id) ON DELETE CASCADE,
                product_id  INTEGER NOT NULL REFERENCES products(id),
                name        TEXT    NOT NULL,   -- snapshot, same reason as sale_lines
                quantity    TEXT    NOT NULL,
                unit_cost   TEXT    NOT NULL,
                line_total  TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_purchase_lines ON purchase_lines(purchase_id);

            -- Money actually handed to the supplier. Kept apart from the purchase so a
            -- 5000 invoice paid 3000 now and 2000 later keeps both payments on record.
            CREATE TABLE IF NOT EXISTS supplier_payments (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                supplier_id INTEGER NOT NULL REFERENCES suppliers(id),
                purchase_id INTEGER          REFERENCES purchases(id),
                amount      TEXT    NOT NULL,
                paid_on     TEXT    NOT NULL,
                method      TEXT    NOT NULL DEFAULT 'Cash',
                note        TEXT    NOT NULL DEFAULT '',
                created_by  INTEGER          REFERENCES workers(id),
                created_at  TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_suppayments_supplier ON supplier_payments(supplier_id);
            CREATE INDEX IF NOT EXISTS ix_suppayments_date     ON supplier_payments(paid_on);

            -- =========================== Expenses ===========================

            CREATE TABLE IF NOT EXISTS expense_categories (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                name      TEXT    NOT NULL UNIQUE,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            -- Operating expenses only: rent, power, water and the like. Stock purchases and
            -- supplier payments deliberately do NOT live here (see the class comment).
            CREATE TABLE IF NOT EXISTS expenses (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                name         TEXT    NOT NULL,
                category_id  INTEGER          REFERENCES expense_categories(id),
                amount       TEXT    NOT NULL,
                spent_on     TEXT    NOT NULL,
                method       TEXT    NOT NULL DEFAULT 'Cash',
                note         TEXT    NOT NULL DEFAULT '',
                receipt_path TEXT,
                recurring    TEXT    NOT NULL DEFAULT 'None',   -- None|Monthly|Weekly|Yearly
                is_void      INTEGER NOT NULL DEFAULT 0,
                created_by   INTEGER          REFERENCES workers(id),
                created_at   TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_expenses_date ON expenses(spent_on);

            -- ========================== Inventory ===========================

            -- Every single change of stock, with the quantity on each side of it. A shop
            -- that cannot say why a count moved cannot find out who is wrong.
            CREATE TABLE IF NOT EXISTS stock_movements (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                product_id   INTEGER NOT NULL REFERENCES products(id),
                moved_at     TEXT    NOT NULL,
                reason       TEXT    NOT NULL,   -- see StockReason
                quantity     TEXT    NOT NULL,   -- signed: negative removes
                before_qty   TEXT    NOT NULL,
                after_qty    TEXT    NOT NULL,
                unit_cost    TEXT    NOT NULL DEFAULT '0',  -- for valuing losses
                reference    TEXT    NOT NULL DEFAULT '',   -- e.g. "Sale #1024", "Purchase #7"
                worker_id    INTEGER          REFERENCES workers(id),
                note         TEXT    NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS ix_movements_product ON stock_movements(product_id);
            CREATE INDEX IF NOT EXISTS ix_movements_date    ON stock_movements(moved_at);

            -- ===================== Shifts and cash drawer ===================

            CREATE TABLE IF NOT EXISTS shifts (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                worker_id     INTEGER NOT NULL REFERENCES workers(id),
                started_at    TEXT    NOT NULL,
                ended_at      TEXT,
                opening_cash  TEXT    NOT NULL DEFAULT '0',
                closing_cash  TEXT,                       -- what was actually counted
                note          TEXT    NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS ix_shifts_worker ON shifts(worker_id);
            CREATE INDEX IF NOT EXISTS ix_shifts_start  ON shifts(started_at);

            -- Cash put in or taken out by hand — a float top-up, a payout, petty cash.
            -- Cash sales are not duplicated here; they are read from the sales table.
            CREATE TABLE IF NOT EXISTS cash_movements (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                shift_id   INTEGER          REFERENCES shifts(id),
                moved_at   TEXT    NOT NULL,
                amount     TEXT    NOT NULL,   -- signed: negative is money out
                reason     TEXT    NOT NULL,
                note       TEXT    NOT NULL DEFAULT '',
                worker_id  INTEGER          REFERENCES workers(id)
            );

            CREATE INDEX IF NOT EXISTS ix_cash_date ON cash_movements(moved_at);

            -- ========================== Audit trail =========================

            CREATE TABLE IF NOT EXISTS activity_log (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                happened_at  TEXT    NOT NULL,
                worker_id    INTEGER          REFERENCES workers(id),
                worker_name  TEXT    NOT NULL DEFAULT '',  -- snapshot: staff leave
                action       TEXT    NOT NULL,
                entity       TEXT    NOT NULL DEFAULT '',
                entity_id    TEXT    NOT NULL DEFAULT '',
                old_value    TEXT    NOT NULL DEFAULT '',
                new_value    TEXT    NOT NULL DEFAULT '',
                detail       TEXT    NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS ix_activity_date ON activity_log(happened_at);

            -- ===================== Returns and refunds ======================

            CREATE TABLE IF NOT EXISTS sale_returns (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                sale_id     INTEGER NOT NULL REFERENCES sales(id),
                returned_at TEXT    NOT NULL,
                amount      TEXT    NOT NULL,
                reason      TEXT    NOT NULL DEFAULT '',
                restock     INTEGER NOT NULL DEFAULT 1,
                worker_id   INTEGER          REFERENCES workers(id),
                note        TEXT    NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS sale_return_lines (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                return_id    INTEGER NOT NULL REFERENCES sale_returns(id) ON DELETE CASCADE,
                sale_line_id INTEGER NOT NULL REFERENCES sale_lines(id),
                product_id   INTEGER          REFERENCES products(id),
                quantity     TEXT    NOT NULL,
                unit_price   TEXT    NOT NULL,
                unit_cost    TEXT    NOT NULL DEFAULT '0',
                line_total   TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_returns_sale ON sale_returns(sale_id);

            -- ---------------------------------------------------------------- the outbox
            --
            -- Sales this machine has taken and the server has not acknowledged yet.
            --
            -- A till must never stop selling because a cable came loose, so every sale is
            -- written here first and handed over afterwards. The reference is the till's own
            -- id for the sale and is unique: handing the same one over twice is how a shop
            -- ends up counting an afternoon's takings twice, and the server recognises a
            -- repeat by this string.
            --
            -- Rows are kept after they are sent rather than deleted. This is the proof of what
            -- left this machine and what the server called it, and a shop that has to ask
            -- "did that sale ever arrive" has nowhere else to look.
            CREATE TABLE IF NOT EXISTS outbox (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                reference   TEXT    NOT NULL UNIQUE,
                payload     TEXT    NOT NULL,
                created_at  TEXT    NOT NULL,
                sent_at     TEXT    NOT NULL DEFAULT '',
                invoice     INTEGER NOT NULL DEFAULT 0,
                attempts    INTEGER NOT NULL DEFAULT 0,
                last_error  TEXT    NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS ix_outbox_waiting ON outbox(sent_at);

            -- A few facts about this database itself: which catalogue it last pulled, and
            -- anything else that describes the file rather than the shop. Kept here rather
            -- than in the settings file, which survives a restored backup and would then
            -- claim a till was up to date when it had just been rolled back a week.
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL DEFAULT ''
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Adds columns to tables that already exist in the field. SQLite has no
    /// "ADD COLUMN IF NOT EXISTS", so each table's real columns are read first — a till
    /// that already holds a month of sales must survive the update with its history intact.
    /// </summary>
    private static void Extend(SqliteConnection connection)
    {
        AddColumns(connection, "products", new[]
        {
            ("cost",        "TEXT    NOT NULL DEFAULT '0'"),
            ("sku",         "TEXT    NOT NULL DEFAULT ''"),
            ("stock",       "TEXT    NOT NULL DEFAULT '0'"),
            ("min_stock",   "TEXT    NOT NULL DEFAULT '0'"),
            ("supplier_id", "INTEGER REFERENCES suppliers(id)"),
            ("shelf",       "TEXT    NOT NULL DEFAULT ''"),
            ("expires_on",  "TEXT"),
            ("show_in_pos", "INTEGER NOT NULL DEFAULT 1"),
            ("updated_at",  "TEXT    NOT NULL DEFAULT ''"),
        });

        AddColumns(connection, "categories", new[]
        {
            ("icon",      "TEXT    NOT NULL DEFAULT ''"),
            ("is_active", "INTEGER NOT NULL DEFAULT 1"),
            // The file name of the picture, not a path: the folder moves with the install,
            // and a stored absolute path would break the first time the shop got a new machine.
            ("image",     "TEXT    NOT NULL DEFAULT ''"),
        });

        AddColumns(connection, "sales", new[]
        {
            // The COGS side of the sale, summed from the lines at save time so reports do
            // not have to re-derive it (and cannot re-derive it differently).
            ("cost_total",   "TEXT    NOT NULL DEFAULT '0'"),
            ("worker_id",    "INTEGER REFERENCES workers(id)"),
            // The till's own id for a sale it handed over. Empty for anything rung up on this
            // machine; unique when set, so a retry after a timeout cannot bank it twice.
            ("till_reference", "TEXT   NOT NULL DEFAULT ''"),
            ("worker_name",  "TEXT    NOT NULL DEFAULT ''"),
            ("shift_id",     "INTEGER REFERENCES shifts(id)"),
            ("status",       "TEXT    NOT NULL DEFAULT 'Completed'"), // Completed|Refunded|PartlyRefunded|Cancelled
            ("refunded",     "TEXT    NOT NULL DEFAULT '0'"),
            ("note",         "TEXT    NOT NULL DEFAULT ''"),
        });

        using (var index = connection.CreateCommand())
        {
            // Partial: only sales that actually came from a till are constrained, so the many
            // rows rung up here with an empty reference do not collide with each other.
            index.CommandText = """
                CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_till_reference
                ON sales(till_reference) WHERE till_reference <> '';
                """;
            index.ExecuteNonQuery();
        }

        AddColumns(connection, "sale_lines", new[]
        {
            ("unit_cost",     "TEXT    NOT NULL DEFAULT '0'"),
            ("returned_qty",  "TEXT    NOT NULL DEFAULT '0'"),
        });

        SeedExpenseCategories(connection);
    }

    private static void AddColumns(SqliteConnection connection, string table,
                                   IReadOnlyList<(string Name, string Type)> columns)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({table});";
            using var reader = info.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(1));
        }

        foreach (var (name, type) in columns)
        {
            if (existing.Contains(name)) continue;
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {name} {type};";
            alter.ExecuteNonQuery();
        }
    }

    /// <summary>The expense kinds every small shop has, so the owner is not typing them in on day one.</summary>
    private static void SeedExpenseCategories(SqliteConnection connection)
    {
        string[] defaults =
        [
            "Rent", "Electricity", "Water", "Internet", "Worker Salaries", "Transportation",
            "Maintenance", "Cleaning", "Equipment", "Repairs", "Taxes", "Packaging",
            "Market Supplies", "Other",
        ];

        foreach (var name in defaults)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO expense_categories (name) VALUES ($name);";
            insert.Parameters.AddWithValue("$name", name);
            insert.ExecuteNonQuery();
        }
    }
}
