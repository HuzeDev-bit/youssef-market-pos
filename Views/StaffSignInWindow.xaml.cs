using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// The lock on the back office. Asks who is opening it, then hands them a window built from
/// what their role allows: the owner gets the whole office, a worker gets the pages they hold
/// — today, just Add product.
///
/// A name is asked for as well as a password because the password alone would only say
/// "somebody knew a password". Everything saved behind this lock moves stock or money, and
/// those records name whoever did it.
///
/// The name box is a list you can also type into. Staff are on the list once the owner gives
/// them a password; the owner is not, because an owner has no staff row — so they type their
/// name and it is remembered. Without that the shop could lock its own owner out simply by
/// giving the first cashier a password.
/// </summary>
public partial class StaffSignInWindow : Window
{
    /// <summary>
    /// One name in the list. <see cref="Worker"/> is null for the owner, who signs in with the
    /// admin password rather than a staff one.
    /// </summary>
    private sealed record Choice(string Name, Worker? Worker);

    private readonly List<Choice> _choices;

    public StaffSignInWindow()
    {
        InitializeComponent();

        _choices = WorkerRepository.List()
            .Where(w => w.HasPin)
            .Select(w => new Choice(w.Name, w))
            .ToList();

        // The owner comes first and is always there, password set or not.
        _choices.Insert(0, new Choice(Session.OwnerLabel, null));

        WorkerBox.ItemsSource = _choices;
        WorkerBox.SelectedIndex = 0;

        // An editable ComboBox has no TextChanged of its own; the one inside its template does.
        WorkerBox.AddHandler(TextBoxBase.TextChangedEvent,
                             new TextChangedEventHandler((_, _) => Retune()));

        Loaded += (_, _) => Retune(focus: true);
    }

    /// <summary>Shows the lock and signs the person in. True when the caller may proceed.</summary>
    public static bool Ask(Window owner)
    {
        if (Session.Current is not null) return true;    // somebody signed in this run
        if (Session.IsOwnerUnlocked) return true;

        return new StaffSignInWindow { Owner = owner }.ShowDialog() == true;
    }

    /// <summary>
    /// Works out who the box is naming. A name matching a listed worker is that worker;
    /// anything else — including a name typed in — is the owner, signing in under it.
    /// </summary>
    private Choice Who()
    {
        var typed = (WorkerBox.Text ?? string.Empty).Trim();

        var worker = _choices.FirstOrDefault(c =>
            c.Worker is not null &&
            string.Equals(c.Name, typed, StringComparison.CurrentCultureIgnoreCase));

        return worker ?? new Choice(typed, null);
    }

    /// <summary>
    /// A password is asked for only when there is one to check. The owner of a shop that has
    /// set no admin password would otherwise be typing into a box that can never be right.
    /// </summary>
    private bool NeedsPassword(Choice who) => who.Worker is not null || AdminAccount.IsConfigured;

    private void Retune(bool focus = false)
    {
        var who = Who();
        var needs = NeedsPassword(who);

        PasswordLabel.Visibility = Visible(needs);
        PasswordBox.Visibility = Visible(needs);

        NoteText.Visibility = Visible(!needs);
        NoteText.Text = needs
            ? string.Empty
            : "No admin password is set, so this opens on a press. Set one under Settings, "
            + "and give your staff their own under Workers.";

        ConfirmButton.Content = needs ? "Sign in" : "Continue";

        if (!focus) return;
        if (needs) PasswordBox.Focus(); else ConfirmButton.Focus();
    }

    private void Worker_Changed(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (IsLoaded) Retune();
    }

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var who = Who();

        if (who.Worker is not null)
        {
            var worker = WorkerRepository.SignIn(who.Worker.Id, PasswordBox.Password);
            if (worker is null) { Fail("Wrong password."); return; }

            Session.SignIn(worker);
            Pass();
            return;
        }

        if (AdminAccount.IsConfigured && !AdminAccount.Verify(PasswordBox.Password))
        {
            Fail("Wrong password.");
            return;
        }

        // The typed name sticks, so it only has to be given once.
        Session.UnlockAsOwner(who.Name);
        Pass();
    }

    private static Visibility Visible(bool yes) => yes ? Visibility.Visible : Visibility.Collapsed;

    private void Pass()
    {
        DialogResult = true;
        Close();
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        PasswordBox.Clear();
        PasswordBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
        else if (e.Key == Key.Enter) SignIn_Click(sender, e);
    }
}
