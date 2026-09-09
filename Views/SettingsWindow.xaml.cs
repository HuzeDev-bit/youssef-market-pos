using System.Windows;
using System.Windows.Input;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// The one thing the shop changes after it is installed: which language the app is in.
///
/// This screen used to hold the shop's name, phone, address, currency, receipt footer, the
/// printer, auto-print and the back-office address. All of them are decided once, when the
/// machine is set up, and every one of them was a way for an owner who opened this window to
/// change the language to walk out having broken their own receipts. They keep whatever they
/// were set to — see <see cref="AppSettings"/>, which still holds and saves every one — they
/// are simply not editable from here any more.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// The languages, in the order they are offered. Held as a plain list so the drop-down can
    /// be filled with strings: a themed ComboBox in this app ignores DisplayMemberPath and asks
    /// each item for a Name, so a box handed objects printed its own type declaration into
    /// itself — which is what the language list was doing, in the one screen a shop opens to
    /// change the language.
    ///
    /// Arabic and French, and no English. The shop is Moroccan and is run in those two; English
    /// is the language the code is written in, not one anybody at this counter reads. It stays
    /// as the source text every translation is keyed by, so nothing is lost by not offering it,
    /// and a screen with a phrase still to be translated falls back to it exactly as before.
    /// </summary>
    private static readonly Models.Language[] Languages =
        [Models.Language.Arabic, Models.Language.French];

    public SettingsWindow()
    {
        InitializeComponent();
        Services.Localizer.Apply(this);
        Services.Responsive.Fit(this);

        // Each language is offered in its own words: a list of languages is read by somebody
        // who does not yet have the app in theirs.
        LanguageBox.ItemsSource = Languages.Select(Loc.NativeName).ToList();

        // A machine left on English by an older build lands on Arabic, which is the first of
        // the two now offered and the shop's own language. It is not silently switched: the
        // box says Arabic from the moment this opens, and nothing is written until Save.
        LanguageBox.SelectedIndex = Math.Max(0, Array.IndexOf(Languages, Loc.Current));

        var printers = ReceiptPrinter.InstalledPrinters();
        PrinterBox.ItemsSource = printers;
        var currentPrinter = AppSettings.Current.ReceiptPrinterName;
        if (!string.IsNullOrWhiteSpace(currentPrinter) && printers.Contains(currentPrinter))
            PrinterBox.SelectedItem = currentPrinter;
        else if (printers.Count > 0)
        {
            var preferred = printers.FirstOrDefault(p => !ReceiptPrinter.IsVirtualPrinter(p) && (p.Contains("pos", StringComparison.OrdinalIgnoreCase) || p.Contains("thermal", StringComparison.OrdinalIgnoreCase) || p.Contains("80", StringComparison.OrdinalIgnoreCase)))
                         ?? printers.FirstOrDefault(p => !ReceiptPrinter.IsVirtualPrinter(p));
            PrinterBox.SelectedItem = preferred ?? printers[0];
        }

        PrinterHint.Text = Loc.T("Receipts will print automatically to this printer.");

        ServerBox.Text = AppSettings.Current.ServerAddress;
        TillBox.Text = AppSettings.Current.TillName;

        Loaded += (_, _) => LanguageBox.Focus();
    }

    /// <summary>Opens settings. True when something was saved.</summary>
    public static bool Ask(Window owner) =>
        new SettingsWindow { Owner = owner }.ShowDialog() == true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var chosen = LanguageBox.SelectedIndex >= 0
            ? Languages[LanguageBox.SelectedIndex]
            : Loc.Current;

        var changed = chosen != Loc.Current;

        AppSettings.Current.Language = Loc.Code(chosen);

        if (PrinterBox.SelectedItem is string selectedPrinter)
        {
            AppSettings.Current.ReceiptPrinterName = selectedPrinter;
        }

        // Typed by hand off a scrap of paper, so the shapes that get typed instead of an
        // address are put right rather than refused: a bare machine name or IP is http on
        // port 5000, and a trailing slash is nothing.
        AppSettings.Current.ServerAddress = Address(ServerBox.Text);
        AppSettings.Current.TillName = TillBox.Text.Trim();

        AppSettings.Current.Save();

        // A language is applied when windows are built, so the ones already open would keep the
        // old one. Rather than leave the shop with half a translated app, offer the restart
        // that actually finishes the job.
        if (changed && Restart()) return;

        DialogResult = true;
        Close();
    }

    /// <summary>Turns what was typed into an address the till can actually call.</summary>
    private static string Address(string? typed)
    {
        var text = (typed ?? string.Empty).Trim().TrimEnd('/');
        if (text.Length == 0) return string.Empty;

        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;

        // A host with no port is the shop server's own, which is the only port this app
        // listens on and the one nobody types.
        return Uri.TryCreate(text, UriKind.Absolute, out var address) && address.IsDefaultPort
            ? $"{address.Scheme}://{address.Host}:5000"
            : text;
    }

    private void TestPrint_Click(object sender, RoutedEventArgs e)
    {
        var sample = new Models.Receipt
        {
            InvoiceNumber = 0,
            SoldAt = DateTime.Now,
            Lines =
            [
                new Models.ReceiptLine { Name = "Test item", Quantity = 1m, Unit = Models.Unit.Each, UnitPrice = 1m, LineTotal = 1m },
            ],
            GrossBeforeDiscount = 1m,
            DiscountKind = Models.DiscountKind.None,
            DiscountValue = 0m,
            DiscountAmount = 0m,
            Subtotal = 1m,
            Tax = 0m,
            Total = 1m,
            PaymentMethod = Models.PaymentMethod.Cash,
            AmountTendered = 1m,
            ChangeGiven = 0m,
        };

        var target = PrinterBox.SelectedItem as string ?? AppSettings.Current.ReceiptPrinterName;
        var prev = AppSettings.Current.ReceiptPrinterName;
        AppSettings.Current.ReceiptPrinterName = target;
        try
        {
            var error = ReceiptPrinter.PrintSilent(sample, isDuplicate: false, allowVirtual: true);
            PrinterHint.Text = error ?? Loc.T("Test receipt sent to {0}.", target);
        }
        finally
        {
            AppSettings.Current.ReceiptPrinterName = prev;
        }
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
