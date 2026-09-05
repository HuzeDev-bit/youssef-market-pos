using System.Windows;
using System.Windows.Input;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// The gate on the back office.
///
/// The password is optional. When none has been set the dialog is a single Unlock button —
/// a shop that has not chosen a password should not be locked out of its own accounts, and
/// pretending otherwise just teaches the owner to write one on the wall. Setting a password
/// in Settings turns the gate on; clearing it turns the gate off again. There is no default
/// password, because a shipped default that nobody changes is the same as no password.
/// </summary>
public partial class AdminLoginWindow : Window
{
    private readonly bool _isChangingPassword;
    private readonly bool _isOpen;

    public AdminLoginWindow(bool changePassword = false)
    {
        InitializeComponent();

        _isChangingPassword = changePassword;
        _isOpen = !changePassword && !AdminAccount.IsConfigured;

        if (_isChangingPassword)
        {
            var replacing = AdminAccount.IsConfigured;
            HeadingText.Text = replacing ? "Change admin password" : "Set admin password";
            SubText.Text = replacing
                ? "Enter the new password twice. The old one stops working straight away."
                : "This will start protecting the back office. Leave both boxes empty and save "
                  + "to turn the password off again.";
            FirstLabel.Text = replacing ? "NEW PASSWORD" : "PASSWORD";
            ConfirmSection.Visibility = Visibility.Visible;
            SubmitButton.Content = replacing ? "Change" : "Set password";
        }
        else if (_isOpen)
        {
            HeadingText.Text = "Back office";
            SubText.Text = "No admin password is set, so anyone at this machine can open the "
                         + "back office. You can set one under Settings → Access.";
            PasswordSection.Visibility = Visibility.Collapsed;
            SubmitButton.Content = "Unlock";
        }
        else
        {
            HeadingText.Text = "Admin";
            SubText.Text = "Enter the admin password to continue.";
            SubmitButton.Content = "Unlock";
        }

        Loaded += (_, _) =>
        {
            if (_isOpen) SubmitButton.Focus();
            else PasswordBox.Focus();
        };
    }

    /// <summary>Shows the prompt; true when the owner is through the gate.</summary>
    public static bool Ask(Window owner, bool changePassword = false) =>
        new AdminLoginWindow(changePassword) { Owner = owner }.ShowDialog() == true;

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        // Nothing to check — no password is set.
        if (_isOpen)
        {
            DialogResult = true;
            Close();
            return;
        }

        var password = PasswordBox.Password;

        if (_isChangingPassword)
        {
            // Both boxes empty is the deliberate way to switch the password off, rather than
            // a hidden toggle somewhere else that could be flipped by accident.
            if (password.Length == 0 && ConfirmBox.Password.Length == 0)
            {
                if (!ConfirmWindow.Ask(this, "Turn the admin password off?",
                        "Anyone at this machine will be able to open the back office, see profit "
                        + "and salaries, and clear the sales history."))
                    return;

                AdminAccount.ClearPassword();
                DialogResult = true;
                Close();
                return;
            }

            if (password.Length < 4)
            {
                Fail("Use at least 4 characters, or leave both boxes empty to turn the password off.");
                return;
            }
            if (password != ConfirmBox.Password)
            {
                Fail("The two passwords do not match.");
                return;
            }

            AdminAccount.SetPassword(password);
            DialogResult = true;
            Close();
            return;
        }

        if (!AdminAccount.Verify(password))
        {
            Fail("Wrong password.");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        PasswordBox.Clear();
        ConfirmBox.Clear();
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
        else if (e.Key == Key.Enter) Submit_Click(sender, e);
    }
}
