using System.Globalization;
using System.Printing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>
/// Renders a receipt for an 80mm thermal roll and sends it to a Windows printer.
///
/// The body is built as plain monospace text, padded to a fixed column width, rather than
/// with FlowDocument Tables. Tables were producing enormous mis-sized rows in the preview,
/// and every thermal printer on earth is a fixed-width character device anyway — padding
/// spaces is both simpler and closer to what the hardware actually does.
///
/// Everything prints black. Thermal printers have no colour, so anything green or grey on
/// screen would only come out as unpredictable dithering on paper.
/// </summary>
public static class ReceiptPrinter
{
    private const double RollWidth = 272;   // ~72mm printable at 96 dpi
    private const int Columns = 40;         // characters per line at 11px monospace

    private static readonly FontFamily Mono = new("Consolas, Courier New, monospace");

    public static string ShopName { get; set; } = "YOUSSEF";

    /// <summary>
    /// Virtual "printers" that write a file instead of putting ink on paper. Every one of
    /// these opens a Save-As dialog, which is the opposite of what a till needs, so they are
    /// never selected automatically.
    /// </summary>
    private static readonly string[] VirtualPrinterMarkers =
    {
        "pdf", "xps", "onenote", "fax", "print to file", "docu", "snagit", "adobe",
    };

    public static bool IsVirtualPrinter(string? name) =>
        name is not null && VirtualPrinterMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sends the receipt straight to the configured printer with no dialog — the cashier must
    /// never pick a printer, and must never be handed a Save-As box, mid-queue.
    ///
    /// Returns null on success, or a short message explaining why nothing printed. A print
    /// failure never takes the sale with it: by the time this runs the money is banked and the
    /// ticket is already in the database.
    /// </summary>
    /// <param name="allowVirtual">
    /// True only for an explicit Test print. Falling back to Print-to-PDF automatically would
    /// throw a Save-As box at the cashier mid-queue, but when someone deliberately asks to test
    /// a file printer they should get their file.
    /// </param>
    public static string? PrintSilent(Receipt receipt, bool isDuplicate, bool allowVirtual = false)
    {
        try
        {
            using var server = new LocalPrintServer();
            var configured = AppSettings.Current.ReceiptPrinterName;

            PrintQueue queue;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    queue = server.GetPrintQueue(configured);
                }
                catch
                {
                    return $"Printer \"{configured}\" not found — check Settings";
                }
            }
            else
            {
                queue = LocalPrintServer.GetDefaultPrintQueue();
                if (queue is null)
                    return "No receipt printer set — choose one in Settings";

                // Refuse to fall back onto Print-to-PDF and friends for a real sale: it would
                // pop a Save-As dialog every time and produce a file instead of a receipt.
                if (IsVirtualPrinter(queue.Name) && !allowVirtual)
                    return $"No thermal printer set. Windows default is \"{queue.Name}\" (a file printer) — pick the real one in Settings";
            }

            var dialog = new PrintDialog { PrintQueue = queue };
            var document = Build(receipt, isDuplicate);
            document.PageWidth = RollWidth;
            document.PageHeight = dialog.PrintableAreaHeight;
            document.ColumnWidth = RollWidth;

            // PrintDocument without ShowDialog: straight to the queue, no UI at all.
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,
                $"Receipt {receipt.InvoiceNumber}{(isDuplicate ? " (duplicate)" : string.Empty)}");
            return null;
        }
        catch (Exception ex)
        {
            return "Could not print: " + ex.Message;
        }
    }

    /// <summary>Every installed printer, for the settings dropdown.</summary>
    public static List<string> InstalledPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            return server.GetPrintQueues().Select(q => q.Name).OrderBy(n => n).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>True when a real, paper-producing printer is ready to receive receipts.</summary>
    public static bool HasUsablePrinter()
    {
        var configured = AppSettings.Current.ReceiptPrinterName;
        if (!string.IsNullOrWhiteSpace(configured))
            return InstalledPrinters().Contains(configured);

        var fallback = DefaultPrinterName();
        return fallback is not null && !IsVirtualPrinter(fallback);
    }

    public static string? DefaultPrinterName()
    {
        try
        {
            return LocalPrintServer.GetDefaultPrintQueue()?.Name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Exactly what the printer gets — used for the on-screen preview too.</summary>
    public static FlowDocument Build(Receipt receipt, bool isDuplicate)
    {
        var doc = new FlowDocument
        {
            FontFamily = Mono,
            FontSize = 11,
            PagePadding = new Thickness(8),
            ColumnWidth = RollWidth,
            PageWidth = RollWidth,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Left,
        };

        var logo = BlackLogo();
        if (logo is not null)
        {
            doc.Blocks.Add(new BlockUIContainer(new Image
            {
                Source = logo,
                Width = 158,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                SnapsToDevicePixels = true,
            })
            {
                Margin = new Thickness(0, 2, 0, 8),
            });
        }

        doc.Blocks.Add(Text(BuildBody(receipt, isDuplicate)));
        return doc;
    }

    /// <summary>
    /// The payment line, in whatever language the shop is set to.
    ///
    /// The French spellings stay as the source text because that is what a Moroccan till
    /// receipt has always said, and they are written without accents on purpose: a thermal
    /// printer driven as raw ESC/POS falls back to codepage 437 and would print "EspÃ¨ces".
    /// </summary>
    private static string MethodLabel(PaymentMethod method) => Loc.T(method switch
    {
        PaymentMethod.Card => "Carte",
        PaymentMethod.Other => "Autre",
        _ => "Especes",
    });

    /// <summary>The receipt as fixed-width text. Public so a future ESC/POS driver can reuse it verbatim.</summary>
    public static string BuildBody(Receipt receipt, bool isDuplicate)
    {
        var sb = new StringBuilder();

        sb.AppendLine(Rule());

        if (isDuplicate)
        {
            // Loud, and repeated at the foot: a copy must never be mistaken for a second
            // sale when the drawer is counted at the end of the shift.
            sb.AppendLine(Centre(Loc.T("*** DUPLICATA / REPRINT ***")));
            sb.AppendLine(Centre(Loc.T("copy - not a new sale")));
            sb.AppendLine(Rule());
        }

        sb.AppendLine(Pair(Loc.T("Ticket N. {0}", receipt.InvoiceNumber),
                            receipt.SoldAt.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture)));
        sb.AppendLine(Rule());

        // Only what the customer actually bought, one line each, quantity underneath.
        foreach (var line in receipt.Lines)
        {
            sb.AppendLine(Pair(Clip(line.Name, Columns - 11), Money(line.LineTotal)));
            sb.AppendLine($"  {line.QuantityLabel} x {Money(line.UnitPrice)}");
        }

        sb.AppendLine(Rule());

        if (receipt.HasDiscount)
        {
            sb.AppendLine(Pair(Loc.T("Sous-total"), Money(receipt.GrossBeforeDiscount)));
            sb.AppendLine(Pair(receipt.DiscountLabel, "-" + Money(receipt.DiscountAmount)));
        }

        // The VAT breakdown belongs on the receipt of a shop that is registered for VAT, and
        // nowhere else. A corner shop that is not registered was printing an HT figure and a
        // TVA figure it does not owe, on every receipt it handed a customer.
        //
        // The tax id in Settings is what says which kind of shop this is: a registered one has
        // an ICE or an IF and has to print it, and an unregistered one has neither.
        if (AppSettings.Current.TaxId.Trim().Length > 0)
        {
            sb.AppendLine(Pair(Loc.T("Total HT"), Money(receipt.Subtotal)));
            sb.AppendLine(Pair(Loc.T("TVA"), Money(receipt.Tax)));
            sb.AppendLine(Rule());
        }

        sb.AppendLine(Pair(Loc.T("TOTAL"), Money(receipt.Total)));
        sb.AppendLine(Rule());

        sb.AppendLine(Pair(MethodLabel(receipt.PaymentMethod),
                           Money(receipt.AmountTendered)));
        if (receipt.PaymentMethod == PaymentMethod.Cash && receipt.ChangeGiven > 0)
            sb.AppendLine(Pair(Loc.T("Rendu"), Money(receipt.ChangeGiven)));

        sb.AppendLine(Rule());
        sb.AppendLine(Centre(Loc.T("Merci et a bientot")));

        if (isDuplicate)
            sb.AppendLine(Centre(Loc.T("*** DUPLICATA / REPRINT ***")));

        return sb.ToString().TrimEnd();
    }

    private static Paragraph Text(string body) => new(new Run(body))
    {
        Margin = new Thickness(0),
        LineHeight = 13,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Foreground = Brushes.Black,
    };

    /// <summary>Label left, amount hard right, padded to the column width.</summary>
    private static string Pair(string left, string right)
    {
        left = Clip(left, Columns - right.Length - 1);
        var gap = Math.Max(1, Columns - left.Length - right.Length);
        return left + new string(' ', gap) + right;
    }

    private static string Centre(string text)
    {
        text = Clip(text, Columns);
        var pad = Math.Max(0, (Columns - text.Length) / 2);
        return new string(' ', pad) + text;
    }

    private static string Rule() => new('-', Columns);

    private static string Clip(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width)];

    private static string Money(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture) + " DH";

    /// <summary>
    /// The wordmark recoloured to solid black, alpha preserved.
    ///
    /// Done by rewriting pixels rather than with an OpacityMask: a mask is resolved at render
    /// time and was producing soft, uneven edges in the preview. A real black bitmap is what
    /// both the screen and a thermal head want, since the head can only burn dots.
    /// </summary>
    private static BitmapSource? BlackLogo()
    {
        if (_blackLogo is not null) return _blackLogo;

        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute);
            source.EndInit();

            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);

            for (var i = 0; i < pixels.Length; i += 4)
            {
                // Bgra32 is premultiplied-free here: zero the colour, keep the alpha.
                pixels[i] = 0;       // B
                pixels[i + 1] = 0;   // G
                pixels[i + 2] = 0;   // R
            }

            var black = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, 96, 96,
                PixelFormats.Bgra32, null, pixels, stride);
            black.Freeze();
            _blackLogo = black;
            return black;
        }
        catch
        {
            return null;   // a receipt without its logo still has to print
        }
    }

    private static BitmapSource? _blackLogo;
}
