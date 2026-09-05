using System.Windows;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// The staff, and what they are paid.
///
/// One page rather than two. A shopkeeper does not think about "workers" and "salaries"
/// separately — they think about Fatima, who works Tuesdays and is owed for last week. Split
/// across two screens, the name is in one place and the money in the other, and paying
/// somebody means holding a figure in your head while you walk between them.
///
/// A password set here is what lets a worker through the back-office lock. They see only the
/// pages their role allows, and everything they save is recorded against their name.
/// </summary>
public partial class WorkersPage : AdminPageBase
{
    /// <summary>A worker with what they are owed for the period sitting on the same row.</summary>
    private sealed class Row
    {
        public required Worker Worker { get; init; }
        public decimal Due { get; init; }
        public decimal Paid { get; init; }
        public decimal Owed => Math.Max(0m, Due - Paid);

        public int Id => Worker.Id;
        public string Name => Worker.Name;
        public string Phone => Worker.Phone;
        public string Initial => Worker.Initial;
        public string RoleLabel => Worker.RoleLabel;
        public bool IsActive => Worker.IsActive;
        public bool IsOwed => Owed > 0m;

        /// <summary>The wage as agreed — "3,000.00 DH a month" — not a figure for this period.</summary>
        public string WageLabel => Worker.Salary <= 0m
            ? "—"
            : $"{Worker.Salary:N2} {Worker.SalaryPeriod switch
            {
                SalaryPeriod.Daily => "a day",
                SalaryPeriod.Weekly => "a week",
                _ => "a month",
            }}";

        public string DueLabel => Due <= 0m ? "—" : $"{Due:N2}";
        public string PaidLabel => Paid <= 0m ? "—" : $"{Paid:N2}";
        public string OwedLabel => Owed <= 0m ? "paid up" : $"{Owed:N2}";
    }

    private List<Row> _rows = new();

    public WorkersPage() => InitializeComponent();

    public override string Title => "Workers";
    public override string Subtitle => "Staff, wages and who can open the back office";
    public override bool UsesDateRange => true;

    protected override void Load()
    {
        Session.Require(Permission.ManageWorkers);

        var staff = WorkerRepository.List(includeInactive: ShowInactive.IsChecked == true);

        // Wages are the owner's business, not a manager's. Without that permission the page
        // still works as a staff list — the money columns simply are not there to read.
        var seesMoney = Session.Can(Permission.SeeSalaries);
        var ledger = seesMoney
            ? WorkerRepository.Ledger(Dates.Range).ToDictionary(l => l.Worker.Id)
            : new Dictionary<int, SalaryLedger>();

        _rows = staff.Select(w => new Row
        {
            Worker = w,
            Due = ledger.GetValueOrDefault(w.Id)?.Due ?? 0m,
            Paid = ledger.GetValueOrDefault(w.Id)?.Paid ?? 0m,
        }).ToList();

        // Whoever is owed the most first: that is the order the owner would ask for.
        _rows = _rows
            .OrderByDescending(r => r.Owed)
            .ThenBy(r => r.Name)
            .ToList();

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;
        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ShowMoney(seesMoney);
        FillSummary(seesMoney);
        ShowGateNote(staff);
    }

    /// <summary>Hides the wage columns outright rather than showing zeroes to somebody who may not see them.</summary>
    private void ShowMoney(bool seesMoney)
    {
        var visible = seesMoney ? Visibility.Visible : Visibility.Hidden;

        MoneyBand.Visibility = seesMoney ? Visibility.Visible : Visibility.Collapsed;
        DueHead.Visibility = visible;
        PaidHead.Visibility = visible;
        OwedHead.Visibility = visible;
    }

    private void FillSummary(bool seesMoney)
    {
        if (!seesMoney) return;

        var live = _rows.Where(r => r.IsActive).ToList();
        var due = live.Sum(r => r.Due);
        var paid = _rows.Sum(r => r.Paid);
        var owed = live.Sum(r => r.Owed);

        CountValue.Text = live.Count.ToString();
        CountNote.Text = _rows.Count > live.Count
            ? Loc.T("{0} no longer here", _rows.Count - live.Count)
            : Loc.T(live.Count == 0 ? "nobody added yet" : "working here");

        DueValue.Text = Money(due);
        DueNote.Text = due <= 0m
            ? Loc.T("no wages agreed yet")
            : Loc.T("for {0}", Loc.T(Dates.RangeLabel).ToLowerInvariant());

        PaidValue.Text = Money(paid);
        PaidNote.Text = due <= 0m
            ? "nothing paid yet"
            // Wages are usually paid for a month while a shorter period is on screen, which
            // puts this well over 100% and reads as an error. Said plainly instead.
            : paid > due ? Loc.T("more than this period's wages")
            : Loc.T("{0}% of what is due", Loc.Ltr($"{paid / due * 100m:0}"));

        OwedValue.Text = Money(owed);
        var owing = live.Count(r => r.IsOwed);
        OwedNote.Text = owing == 0
            ? Loc.T("everyone is paid up")
            : Loc.T(owing == 1 ? "to {0} worker" : "to {0} workers", owing);
    }

    private void ShowGateNote(List<Worker> staff)
    {
        var withPassword = staff.Count(w => w.IsActive && w.HasPin);

        GateNote.Text = withPassword == 0
            ? Loc.T("No worker has a password yet, so only the owner can open the back office.")
            : Loc.T("{0} can open the back office · each sees only the pages their role allows",
                    withPassword);
    }

    private static string Money(decimal amount) =>
        Loc.Ltr($"{amount:N2} {AppSettings.Current.Currency}");

    // ============================== Actions ==============================

    private Row? Find(object sender) =>
        sender is FrameworkElement { Tag: int id } ? _rows.FirstOrDefault(r => r.Id == id) : null;

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is not null && WorkerWindow.AddNew(Shell)) ReloadAll();
    }

    private void Row_Click(object sender, RoutedEventArgs e) => Edit_Click(sender, e);

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || Find(sender) is not { } row) return;
        if (WorkerWindow.Edit(Shell, row.Worker)) ReloadAll();
    }

    /// <summary>
    /// Sets the password this person types at the lock. Stored as a salted hash like the
    /// owner's, so it can be replaced but never read back.
    /// </summary>
    private void Password_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || Find(sender) is not { } row) return;

        var password = PinWindow.Ask(Shell, row.Name, row.Worker.HasPin);
        if (password is null) return;

        WorkerRepository.SetPin(row.Id, password);
        ReloadAll();
    }

    /// <summary>
    /// Pays wages. Suggested at what is still owed for the period, and capped there — paying
    /// somebody more than they are owed is a typo, not a decision.
    ///
    /// The payment is recorded against the period on screen, so paying "this month" while
    /// looking at last month books it to last month, which is what was intended.
    /// </summary>
    private void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || Find(sender) is not { } row) return;

        if (!Session.Can(Permission.PaySalaries))
        {
            ConfirmWindow.Ask(Shell, "Not allowed",
                $"{Session.CurrentName} may not record salary payments.");
            return;
        }

        var result = AmountWindow.Ask(Shell, new AmountRequest
        {
            Heading = $"Pay {row.Name}",
            Blurb = row.Owed > 0m
                ? $"{Money(row.Owed)} owed for {Dates.RangeLabel.ToLowerInvariant()}."
                : $"Nothing outstanding for {Dates.RangeLabel.ToLowerInvariant()}.",
            AmountLabel = "AMOUNT PAID",
            ConfirmText = "Record payment",
            Suggested = row.Owed > 0m ? row.Owed : null,
            Maximum = row.Owed > 0m ? row.Owed : null,
        });

        if (result is null) return;

        WorkerRepository.PaySalary(row.Id, row.Name, row.Due, result.Amount,
                                   Dates.Range, result.Date, result.Method, result.Note);
        ReloadAll();
    }
}
