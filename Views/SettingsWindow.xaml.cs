using System.Windows;
using System.Windows.Input;
using MarketPos.Models;
using MarketPos.Services;
using System.Linq;

namespace MarketPos.Views;

/// <summary>
/// The shop's own details, and how it prints.
///
/// The name, phone, address and currency are on every receipt a customer takes away and at
/// the top of every report — so they belong to the shop, not to the code. They were hard-coded
/// defaults until now, which meant a Moroccan grocer's receipts said "Market".
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>One row of the language list: the enum, and what that language calls itself.</summary>
    private sealed record LanguageChoice(Language Language, string Label);

    private const string UseDefault = "(Windows default printer)";
    private const string FileSuffix = "   — saves a file, not a receipt";

    public SettingsWindow()
    {
        InitializeComponent();

        PrinterBox.Items.Add(UseDefault);
        foreach (var name in ReceiptPrinter.InstalledPrinters())
        {
            // Marked, not hidden: the owner may genuinely want a PDF copy on a back-office
            // machine, but they should never pick one for the till by accident.
            PrinterBox.Items.Add(ReceiptPrinter.IsVirtualPrinter(name) ? name + FileSuffix : name);
        }

        var configured = AppSettings.Current.ReceiptPrinterName;
        PrinterBox.SelectedItem = PrinterBox.Items.Cast<string>()
            .FirstOrDefault(i => i.Replace(FileSuffix, string.Empty) == configured) ?? UseDefault;

        var fallback = ReceiptPrinter.DefaultPrinterName();
        DefaultHint.Text = fallback is null
            ? Loc.T("Windows reports no default printer on this machine.")
            : ReceiptPrinter.IsVirtualPrinter(fallback)
                ? Loc.T("Windows default is \"{0}\", which saves a file instead of printing. "
                      + "Receipts will NOT print automatically until a real receipt printer is "
                      + "selected above.", fallback)
                : Loc.T("Windows default is currently: {0}", fallback);

        AutoPrintBox.IsChecked = AppSettings.Current.AutoPrintReceipts;

        ShopNameBox.Text = AppSettings.Current.BusinessName;
        ShopPhoneBox.Text = AppSettings.Current.BusinessPhone;
        ShopAddressBox.Text = AppSettings.Current.BusinessAddress;
        CurrencyBox.Text = AppSettings.Current.Currency;
        FooterBox.Text = AppSettings.Current.ReceiptFooter;

        // Each language is offered in its own words: a list of languages is read by somebody
        // who does not yet have the app in theirs.
        LanguageBox.ItemsSource = Enum.GetValues<Language>()
            .Select(l => new LanguageChoice(l, Loc.NativeName(l)))
            .ToList();
        LanguageBox.DisplayMemberPath = nameof(LanguageChoice.Label);
        LanguageBox.SelectedItem = (LanguageBox.ItemsSource as List<LanguageChoice>)!
            .First(c => c.Language == Loc.Current);

        ServerBox.Text = AppSettings.Current.ServerAddress;
        TillNameBox.Text = AppSettings.Current.TillLabel;

        Loaded += (_, _) => { ShopNameBox.Focus(); ShopNameBox.SelectAll(); };
    }

    /// <summary>Opens settings. True when something was saved.</summary>
    public static bool Ask(Window owner) =>
        new SettingsWindow { Owner = owner }.ShowDialog() == true;

    private string SelectedPrinter =>
        PrinterBox.SelectedItem as string is { } name && name != UseDefault
            ? name.Replace(FileSuffix, string.Empty)
            : string.Empty;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = ShopNameBox.Text.Trim();
        if (name.Length == 0)
        {
            StatusText.Text = "Give the shop a name — it goes on every receipt.";
            ShopNameBox.Focus();
            return;
        }

        var currency = CurrencyBox.Text.Trim();
        if (currency.Length == 0)
        {
            StatusText.Text = "Every amount needs a currency after it.";
            CurrencyBox.Focus();
            return;
        }

        AppSettings.Current.BusinessName = name;
        AppSettings.Current.BusinessPhone = ShopPhoneBox.Text.Trim();
        AppSettings.Current.BusinessAddress = ShopAddressBox.Text.Trim();
        AppSettings.Current.Currency = currency;
        AppSettings.Current.ReceiptFooter = FooterBox.Text.Trim();

        AppSettings.Current.ReceiptPrinterName = SelectedPrinter;
        AppSettings.Current.AutoPrintReceipts = AutoPrintBox.IsChecked == true;

        AppSettings.Current.ServerAddress = Address;
        AppSettings.Current.TillName = TillNameBox.Text.Trim();

        var chosen = (LanguageBox.SelectedItem as LanguageChoice)?.Language ?? Loc.Current;
        var languageChanged = chosen != Loc.Current;
        AppSettings.Current.Language = Loc.Code(chosen);

        AppSettings.Current.Save();

        // A language is applied when windows are built, so the ones already open would keep
        // the old one. Rather than leave the shop with half a translated app, offer the
        // restart that actually finishes the job.
        if (languageChanged && Restart()) return;

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// What was typed, made into an address. A shopkeeper reading an IP off another screen
    /// types "192.168.1.20:5000", and refusing that for want of "http://" would be the
    /// software being pedantic at somebody who did exactly the right thing.
    /// </summary>
    private string Address
    {
        get
        {
            var text = ServerBox.Text.Trim().TrimEnd('/');
            if (text.Length == 0) return string.Empty;
            return text.Contains("://") ? text : $"http://{text}";
        }
    }

    /// <summary>
    /// Asks the address whether there is a shop behind it, before the owner walks away
    /// believing there is. Uses what is on screen, and saves nothing.
    /// </summary>
    private async void TestLink_Click(object sender, RoutedEventArgs e)
    {
        var typed = Address;
        if (typed.Length == 0)
        {
            StatusText.Text = "With no address this machine works on its own — which is right "
                            + "for a shop with one computer.";
            return;
        }

        var previous = AppSettings.Current.ServerAddress;
        AppSettings.Current.ServerAddress = typed;
        StatusText.Text = $"Asking {typed}…";

        try
        {
            StatusText.Text = await ShopLink.Ping()
                ? $"Found {ShopLink.ShopName} at {typed}. This till will send its sales there."
                : ShopLink.LastProblem;
        }
        finally
        {
            AppSettings.Current.ServerAddress = previous;   // nothing is saved until Save
        }
    }

    /// <summary>
    /// Prints a throwaway receipt so the printer can be proven before a real customer is
    /// standing at the till. Uses a fake ticket number and never touches the database.
    /// </summary>
    private void TestPrint_Click(object sender, RoutedEventArgs e)
    {
        var previous = AppSettings.Current.ReceiptPrinterName;
        AppSettings.Current.ReceiptPrinterName = SelectedPrinter;   // test what is on screen

        var sample = new Receipt
        {
            InvoiceNumber = 0,
            SoldAt = DateTime.Now,
            Lines = new[]
            {
                new ReceiptLine { Name = "Test item", Quantity = 1m, Unit = Unit.Each, UnitPrice = 1m, LineTotal = 1m },
            },
            GrossBeforeDiscount = 1m,
            DiscountKind = DiscountKind.None,
            DiscountValue = 0m,
            DiscountAmount = 0m,
            Subtotal = 1m,
            Tax = 0m,
            Total = 1m,
            PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 1m,
            ChangeGiven = 0m,
        };

        // allowVirtual: a test print is deliberate, so Print-to-PDF is fair game here even
        // though it is refused for real sales.
        var target = SelectedPrinter;
        if (string.IsNullOrWhiteSpace(target)) target = ReceiptPrinter.DefaultPrinterName() ?? "the default printer";

        var error = ReceiptPrinter.PrintSilent(sample, isDuplicate: false, allowVirtual: true);
        StatusText.Text = error
            ?? (ReceiptPrinter.IsVirtualPrinter(target)
                ? $"Sent to {target} — it will ask you where to save the file."
                : $"Test sent to {target}.");

        AppSettings.Current.ReceiptPrinterName = previous;   // nothing is saved until Save
    }

    /// <summary>
    /// Starts the app again in the new language and closes this one. Refused politely if the
    /// shop says no — the setting is already saved either way, so the language arrives the
    /// next time the till is opened.
    /// </summary>
    private bool Restart()
    {
        if (!ConfirmWindow.Ask(this,
                Loc.T("Saved. Restart the app to see it in the new language."),
                Loc.T("Restart now")))
            return false;

        var exe = Environment.ProcessPath;
        if (exe is null) return false;

        System.Diagnostics.Process.Start(exe);
        Application.Current.Shutdown();
        return true;
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
