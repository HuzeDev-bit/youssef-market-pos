using System.Windows;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>Add or edit a supplier. Deactivating never deletes — past invoices point here.</summary>
public partial class SupplierWindow : Window
{
    private readonly Supplier? _existing;

    /// <summary>
    /// The row that was just created, so the caller can go straight on to recording what they
    /// brought. Zero when a supplier was edited rather than added.
    /// </summary>
    private int _created;

    public SupplierWindow(Supplier? existing)
    {
        InitializeComponent();
        _existing = existing;

        if (existing is null)
        {
            HeadingText.Text = "Add supplier";
            SubText.Text = "Only the name is required. Put in what they brought below and it is "
                         + "recorded with them.";

            // The goods editor needs the width; a contact form on its own does not.
            Width = 940;
            GoodsSection.Visibility = Visibility.Visible;
            MethodBox.ItemsSource = new[] { "Cash", "Bank transfer", "Cheque", "Card", "Credit \u2014 pay later" };
            MethodBox.SelectedIndex = 0;
            PaidBox.Text = "0";
            ShowTotal();
        }
        else
        {
            HeadingText.Text = existing.Name;
            SubText.Text = "Editing a supplier does not change any invoice already recorded.";
            NameBox.Text = existing.Name;
            ContactBox.Text = existing.Contact;
            PhoneBox.Text = existing.Phone;
            EmailBox.Text = existing.Email;
            AddressBox.Text = existing.Address;
            NoteBox.Text = existing.Note;
            DeactivateButton.Visibility = Visibility.Visible;

            BalanceCard.Visibility = Visibility.Visible;
            BalanceText.Text = existing.Owed > 0m
                ? $"{existing.Owed:N2} DH still owed"
                : "Nothing outstanding";
            BalanceNote.Text = $"{existing.TotalPurchased:N2} DH purchased, {existing.TotalPaid:N2} DH paid.";
        }

        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    /// <summary>
    /// Adds a supplier. <paramref name="createdId"/> carries the new row's id, because a
    /// supplier is almost never added in the abstract — somebody is standing there with a
    /// delivery, and the next thing to record is what was in it.
    /// </summary>
    public static bool AddNew(Window owner, out int createdId)
    {
        var window = new SupplierWindow(null) { Owner = owner };
        var saved = window.ShowDialog() == true;
        createdId = saved ? window._created : 0;
        return saved;
    }

    public static bool AddNew(Window owner) => AddNew(owner, out _);

    public static bool Edit(Window owner, Supplier supplier) =>
        new SupplierWindow(supplier) { Owner = owner }.ShowDialog() == true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = "Give the supplier a name.";
            NameBox.Focus();
            return;
        }

        var supplier = new Supplier
        {
            Id = _existing?.Id ?? 0,
            Name = name,
            Contact = ContactBox.Text.Trim(),
            Phone = PhoneBox.Text.Trim(),
            Email = EmailBox.Text.Trim(),
            Address = AddressBox.Text.Trim(),
            Note = NoteBox.Text.Trim(),
        };

        var lines = _existing is null ? Editor.Lines.ToList() : new List<PurchaseLine>();

        DeliveryEditor.TryMoney(PaidBox?.Text, out var paid);
        if (lines.Count > 0)
        {
            var total = lines.Sum(l => l.LineTotal);
            if (paid < 0m)
            {
                ErrorText.Text = "The amount paid cannot be negative.";
                return;
            }
            if (paid > total)
            {
                ErrorText.Text = $"You cannot pay more than the {total:N2} DH delivery.";
                return;
            }

            var atALoss = Editor.BelowCost;
            if (atALoss.Count > 0 &&
                !ConfirmWindow.Ask(this,
                    atALoss.Count == 1
                        ? $"Sell {atALoss[0].Name} below what it cost?"
                        : $"Sell {atALoss.Count} of these below what they cost?",
                    "Every one sold will lose money. Sometimes that is deliberate \u2014 confirm if it is."))
                return;
        }

        try
        {
            if (_existing is null) _created = SupplierRepository.Create(supplier);
            else SupplierRepository.Update(supplier);

            // The delivery is recorded second and separately: the supplier row has to exist
            // for it to point at. If it throws, the supplier is still saved and the goods can
            // be entered again from the page rather than the whole thing being lost.
            if (lines.Count > 0)
            {
                SupplierRepository.RecordPurchase(new Purchase
                {
                    SupplierId = _created,
                    SupplierName = supplier.Name,
                    PurchasedOn = DateTime.Today,
                    Method = MethodBox.SelectedItem as string ?? "Cash",
                    Lines = lines,
                }, paid);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    // ============================== What they brought ==============================

    private void Editor_Problem(object? sender, string problem) => ErrorText.Text = problem;

    private void Editor_Changed(object? sender, EventArgs e)
    {
        ErrorText.Text = string.Empty;
        ShowTotal();
    }

    private void Paid_Changed(object sender, RoutedEventArgs e) => ShowTotal();

    private void ShowTotal()
    {
        if (TotalText is null || Editor is null) return;

        var total = Editor.Total;
        TotalText.Text = Loc.Ltr($"{total:N2} DH");

        DeliveryEditor.TryMoney(PaidBox.Text, out var paid);
        var remaining = total - paid;

        OwingText.Text = Editor.Lines.Count == 0
            ? "Add what arrived, or leave it empty."
            : remaining <= 0m
                ? "Paid in full \u2014 nothing will be owed."
                : $"{remaining:N2} DH will be owed to them.";
    }

    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null) return;

        // A supplier with an unpaid balance quietly disappearing from the list is how a debt
        // gets forgotten, so the warning names the figure rather than being generic.
        var body = _existing.Owed > 0m
            ? $"{_existing.Owed:N2} DH is still owed to them. They stop appearing in lists, "
              + "but the debt and every invoice stay on record."
            : "They stop appearing in lists. Nothing is deleted.";

        if (!ConfirmWindow.Ask(this, $"Deactivate {_existing.Name}?", body)) return;

        SupplierRepository.SetActive(_existing.Id, _existing.Name, active: false);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Backing out of a half-entered delivery loses real typing, so it asks first.
        var count = _existing is null ? Editor.Lines.Count : 0;
        if (count > 0 &&
            !ConfirmWindow.Ask(this, "Discard this supplier?",
                $"The name and {count} delivery line{(count == 1 ? string.Empty : "s")} will be lost."))
            return;

        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Cancel_Click(sender, e); return; }

        // Enter with the cursor in a price box means "add this line", not "save the supplier
        // and close" — which is what it would otherwise do, halfway through typing a delivery.
        if (e.Key == Key.Enter && _existing is null && Editor.WantsEnter)
        {
            Editor_Problem(this, Editor.AddLine() ?? string.Empty);
            e.Handled = true;
        }
    }
}
