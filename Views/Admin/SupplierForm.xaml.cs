using MarketPos.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// A supplier's details, and what they brought when they are new.
///
/// On its own so it can be shown in two places: in place of the list on the Suppliers page
/// when a supplier is added — a page of its own, not a window over the page — and inside
/// <see cref="SupplierWindow"/> when one is edited. Deactivating never deletes; past
/// invoices point here.
/// </summary>
public partial class SupplierForm : UserControl
{
    private readonly Supplier? _existing;

    public SupplierForm(Supplier? existing)
    {
        InitializeComponent();
        _existing = existing;

        if (existing is null)
        {
            HeadingText.Text = Loc.T("Add supplier");
            SubText.Text = Loc.T("Only the name is required. Put in what they brought below "
                               + "and it is recorded with them.");

            GoodsSection.Visibility = Visibility.Visible;
            MethodBox.ItemsSource = new[]
            {
                Loc.T("Cash"), Loc.T("Bank transfer"), Loc.T("Cheque"),
                Loc.T("Card"), Loc.T("Credit — pay later"),
            };
            MethodBox.SelectedIndex = 0;
            PaidBox.Text = "0";
            ShowTotal();
        }
        else
        {
            HeadingText.Text = existing.Name;
            SubText.Text = Loc.T("Editing a supplier does not change any invoice already recorded.");
            NameBox.Text = existing.Name;
            ContactBox.Text = existing.Contact;
            PhoneBox.Text = existing.Phone;
            EmailBox.Text = existing.Email;
            AddressBox.Text = existing.Address;
            NoteBox.Text = existing.Note;
            DeactivateButton.Visibility = Visibility.Visible;

            // A hidden supplier's button brings them back instead. Without it the only way to
            // undo Deactivate was to add them again, as somebody new with none of their history.
            if (!existing.IsActive)
            {
                DeactivateButton.Content = Loc.T("Reactivate");
                DeactivateButton.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Accent");
            }

            BalanceCard.Visibility = Visibility.Visible;
            BalanceText.Text = existing.Owed > 0m
                ? $"{existing.Owed:N2} DH still owed"
                : "Nothing outstanding";
            BalanceNote.Text = $"{existing.TotalPurchased:N2} DH purchased, {existing.TotalPaid:N2} DH paid.";
        }

        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    /// <summary>Raised when the form is finished with: true if something was saved.</summary>
    public event EventHandler<bool>? Done;

    /// <summary>
    /// The row that was just created, so the caller can go straight on to what they brought.
    /// Zero when a supplier was edited rather than added.
    /// </summary>
    public int Created { get; private set; }

    /// <summary>Whatever is holding the form, for the questions it has to ask.</summary>
    private Window Host => Window.GetWindow(this)!;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = Loc.T("Give the supplier a name.");
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
                ErrorText.Text = Loc.T("The amount paid cannot be negative.");
                return;
            }
            if (paid > total)
            {
                ErrorText.Text = $"You cannot pay more than the {total:N2} DH delivery.";
                return;
            }

            var atALoss = Editor.BelowCost;
            if (atALoss.Count > 0 &&
                !ConfirmWindow.Ask(Host,
                    atALoss.Count == 1
                        ? $"Sell {atALoss[0].Name} below what it cost?"
                        : $"Sell {atALoss.Count} of these below what they cost?",
                    "Every one sold will lose money. Sometimes that is deliberate — confirm if it is."))
                return;
        }

        try
        {
            if (_existing is null) Created = Link.Shop.Suppliers.Create(supplier);
            else Link.Shop.Suppliers.Update(supplier);

            // The delivery is recorded second and separately: the supplier row has to exist
            // for it to point at. If it throws, the supplier is still saved and the goods can
            // be entered again from the page rather than the whole thing being lost.
            if (lines.Count > 0)
            {
                Link.Shop.Suppliers.RecordPurchase(new Purchase
                {
                    SupplierId = Created,
                    SupplierName = supplier.Name,
                    PurchasedOn = DateTime.Today,
                    Method = MethodBox.SelectedItem as string ?? "Cash",
                    Lines = lines,
                }, paid);
            }

            Done?.Invoke(this, true);
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
            ? Loc.T("Add what arrived, or leave it empty.")
            : remaining <= 0m
                ? Loc.T("Paid in full — nothing will be owed.")
                : Loc.T("{0} will be owed to them.", Loc.Ltr($"{remaining:N2} DH"));
    }

    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null) return;

        if (!_existing.IsActive)
        {
            Link.Shop.Suppliers.SetActive(_existing.Id, _existing.Name, active: true);
            Done?.Invoke(this, true);
            return;
        }

        // A supplier with an unpaid balance quietly disappearing from the list is how a debt
        // gets forgotten, so the warning names the figure rather than being generic.
        var body = _existing.Owed > 0m
            ? $"{_existing.Owed:N2} DH is still owed to them. They stop appearing in lists, "
              + "but the debt and every invoice stay on record."
            : "They stop appearing in lists. Nothing is deleted.";

        if (!ConfirmWindow.Ask(Host, $"Deactivate {_existing.Name}?", body)) return;

        Link.Shop.Suppliers.SetActive(_existing.Id, _existing.Name, active: false);
        Done?.Invoke(this, true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

    /// <summary>Backs out. A half-entered delivery is real typing, so it asks before losing it.</summary>
    public void Cancel()
    {
        var count = _existing is null ? Editor.Lines.Count : 0;
        if (count > 0 &&
            !ConfirmWindow.Ask(Host, "Discard this supplier?",
                $"The name and {count} delivery line{(count == 1 ? string.Empty : "s")} will be lost."))
            return;

        Done?.Invoke(this, false);
    }

    private void Form_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // An open dropdown takes Escape for itself: it closes the list, not the form.
            if (Mouse.Captured is ComboBox { IsDropDownOpen: true }) return;

            Cancel();
            e.Handled = true;
            return;
        }

        // Enter with the cursor in a price box means "add this line", not "save the supplier
        // and close" — which is what it would otherwise do, halfway through typing a delivery.
        if (e.Key == Key.Enter && _existing is null && Editor.WantsEnter)
        {
            Editor_Problem(this, Editor.AddLine() ?? string.Empty);
            e.Handled = true;
        }
    }
}
