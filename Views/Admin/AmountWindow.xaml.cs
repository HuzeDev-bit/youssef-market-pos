using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace MarketPos.Views.Admin;

/// <summary>What the caller wants asked.</summary>
public sealed class AmountRequest
{
    public required string Heading { get; init; }
    public string Blurb { get; init; } = string.Empty;
    public string AmountLabel { get; init; } = "AMOUNT";
    public string ConfirmText { get; init; } = "Save";

    /// <summary>Pre-filled amount — usually the outstanding balance, since that is what is normally paid.</summary>
    public decimal? Suggested { get; init; }

    /// <summary>Upper bound, when paying more than this would be meaningless.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>False for things that are not a payment, so no "paid by" is asked for.</summary>
    public bool AskMethod { get; init; } = true;

    /// <summary>Allows negatives — the cash drawer needs to take money out as well as put it in.</summary>
    public bool AllowNegative { get; init; }
}

public sealed record AmountResult(decimal Amount, DateTime Date, string Method, string Note);

/// <summary>
/// One dialog for every "how much, when, and how" question in the back office: paying a
/// supplier, paying a salary, moving cash in or out of the drawer.
///
/// These are the same question with a different heading, and three near-identical dialogs
/// would drift apart the first time one of them gained a validation rule.
/// </summary>
public partial class AmountWindow : Window
{
    private readonly AmountRequest _request;

    public AmountWindow(AmountRequest request)
    {
        InitializeComponent();
        _request = request;

        Title = request.Heading;
        HeadingText.Text = request.Heading;
        BlurbText.Text = request.Blurb;
        BlurbText.Visibility = request.Blurb.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AmountLabel.Text = request.AmountLabel;
        ConfirmButton.Content = request.ConfirmText;

        DateBox.SelectedDate = DateTime.Today;
        MethodBox.ItemsSource = new[] { "Cash", "Bank transfer", "Cheque", "Card", "Other" };
        MethodBox.SelectedIndex = 0;
        MethodSection.Visibility = request.AskMethod ? Visibility.Visible : Visibility.Collapsed;

        if (request.Suggested is { } suggested)
            AmountBox.Text = suggested.ToString("0.00", CultureInfo.InvariantCulture);

        Loaded += (_, _) => { AmountBox.Focus(); AmountBox.SelectAll(); };
    }

    /// <summary>Shows the dialog; null when the owner backed out.</summary>
    public static AmountResult? Ask(Window owner, AmountRequest request)
    {
        var window = new AmountWindow(request) { Owner = owner };
        return window.ShowDialog() == true ? window.Result : null;
    }

    public AmountResult? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountBox.Text.Trim().Replace(',', '.'),
                              NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            ErrorText.Text = "Enter an amount, like 250 or 250.50.";
            AmountBox.Focus();
            return;
        }

        if (amount == 0m)
        {
            ErrorText.Text = "Enter an amount other than zero.";
            return;
        }

        if (amount < 0m && !_request.AllowNegative)
        {
            ErrorText.Text = "The amount cannot be negative.";
            return;
        }

        if (_request.Maximum is { } max && amount > max)
        {
            ErrorText.Text = $"That is more than the {max:N2} DH outstanding.";
            return;
        }

        Result = new AmountResult(
            amount,
            DateBox.SelectedDate ?? DateTime.Today,
            _request.AskMethod ? MethodBox.SelectedItem as string ?? "Cash" : "Cash",
            NoteBox.Text.Trim());

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
        else if (e.Key == Key.Enter) Save_Click(sender, e);
    }
}
