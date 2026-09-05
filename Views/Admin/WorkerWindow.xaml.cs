using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Add or edit a member of staff.
///
/// The role box spells out what it grants, because "Manager" is not self-explanatory and
/// getting it wrong is how a cashier ends up able to see the shop's profit.
/// </summary>
public partial class WorkerWindow : Window
{
    private readonly Worker? _existing;

    private static readonly (string Label, WorkerRole Role, string What)[] Roles =
    [
        ("Cashier", WorkerRole.Cashier,
         "Uses the till and can see their own sales. Nothing else in the back office."),
        ("Stock worker", WorkerRole.StockWorker,
         "Manages products and stock levels, and can see stock movements. No money screens."),
        ("Manager", WorkerRole.Manager,
         "Runs the shop floor: products, stock, suppliers, purchases, staff and reports. "
         + "Cannot see profit, salaries, supplier debt or settings."),
        ("Owner", WorkerRole.Owner,
         "Everything, including profit, salaries, supplier debt and business settings."),
    ];

    public WorkerWindow(Worker? existing)
    {
        InitializeComponent();
        _existing = existing;

        RoleBox.ItemsSource = Roles.Select(r => r.Label).ToList();
        PeriodBox.ItemsSource = new[] { "Monthly", "Weekly", "Daily" };

        // Salary is not shown at all to someone who may not see salaries; leaving an empty
        // box there would invite them to type one in and have it silently rejected.
        SalarySection.Visibility = Session.Can(Permission.SeeSalaries)
            ? Visibility.Visible : Visibility.Collapsed;

        if (existing is null)
        {
            HeadingText.Text = "Add worker";
            SubText.Text = "Give them a role now; a till PIN can be set afterwards.";
            RoleBox.SelectedIndex = 0;
            PeriodBox.SelectedIndex = 0;
            StartedBox.SelectedDate = DateTime.Today;
        }
        else
        {
            HeadingText.Text = existing.Name;
            SubText.Text = existing.IsActive
                ? $"{existing.RoleLabel} since {existing.StartedOn:d MMMM yyyy}."
                : "This worker is inactive.";
            NameBox.Text = existing.Name;
            PhoneBox.Text = existing.Phone;
            EmailBox.Text = existing.Email;
            NoteBox.Text = existing.Note;
            StartedBox.SelectedDate = existing.StartedOn;
            SalaryBox.Text = existing.Salary.ToString("0.00", CultureInfo.InvariantCulture);
            RoleBox.SelectedIndex = Math.Max(0, Array.FindIndex(Roles, r => r.Role == existing.Role));
            PeriodBox.SelectedIndex = existing.SalaryPeriod switch
            {
                SalaryPeriod.Weekly => 1,
                SalaryPeriod.Daily => 2,
                _ => 0,
            };

            ActiveButton.Visibility = Visibility.Visible;
            ActiveButton.Content = existing.IsActive ? "Deactivate" : "Reactivate";
            if (existing.IsActive)
                ActiveButton.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger");
        }

        UpdateRoleNote();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    public static bool AddNew(Window owner) =>
        new WorkerWindow(null) { Owner = owner }.ShowDialog() == true;

    public static bool Edit(Window owner, Worker worker) =>
        new WorkerWindow(worker) { Owner = owner }.ShowDialog() == true;

    private void Role_Changed(object sender, RoutedEventArgs e) => UpdateRoleNote();

    private void UpdateRoleNote()
    {
        if (RoleNoteText is null) return;
        RoleNoteText.Text = Roles[Math.Max(0, RoleBox.SelectedIndex)].What;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = "Give the worker a name.";
            NameBox.Focus();
            return;
        }

        var salary = _existing?.Salary ?? 0m;
        if (SalarySection.Visibility == Visibility.Visible)
        {
            if (SalaryBox.Text.Trim().Length > 0 &&
                !decimal.TryParse(SalaryBox.Text.Trim().Replace(',', '.'),
                                  NumberStyles.Number, CultureInfo.InvariantCulture, out salary))
            {
                ErrorText.Text = "The salary must be a number, like 3000.";
                SalaryBox.Focus();
                return;
            }
            if (salary < 0m) { ErrorText.Text = "The salary cannot be negative."; return; }
        }

        var worker = new Worker
        {
            Id = _existing?.Id ?? 0,
            Name = name,
            Phone = PhoneBox.Text.Trim(),
            Email = EmailBox.Text.Trim(),
            Role = Roles[Math.Max(0, RoleBox.SelectedIndex)].Role,
            StartedOn = StartedBox.SelectedDate ?? DateTime.Today,
            Salary = salary,
            SalaryPeriod = PeriodBox.SelectedIndex switch
            {
                1 => SalaryPeriod.Weekly,
                2 => SalaryPeriod.Daily,
                _ => SalaryPeriod.Monthly,
            },
            Note = NoteBox.Text.Trim(),
            IsActive = _existing?.IsActive ?? true,
        };

        try
        {
            if (_existing is null) WorkerRepository.Create(worker);
            else WorkerRepository.Update(worker);

            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    private void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null) return;
        var activate = !_existing.IsActive;

        if (!activate && !ConfirmWindow.Ask(this, $"Deactivate {_existing.Name}?",
                "They can no longer sign in at the till. Their past sales, shifts and salary "
                + "payments all stay on record."))
            return;

        WorkerRepository.SetActive(_existing.Id, _existing.Name, activate);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
    }
}
