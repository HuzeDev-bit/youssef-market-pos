using System.Globalization;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
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

    public static bool IsThermalPrinter(PrintQueue queue)
    {
        var name = queue.Name;
        var driver = queue.QueueDriver?.Name ?? string.Empty;
        return name.Contains("pos", StringComparison.OrdinalIgnoreCase)
            || name.Contains("thermal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("receipt", StringComparison.OrdinalIgnoreCase)
            || name.Contains("80", StringComparison.OrdinalIgnoreCase)
            || name.Contains("58", StringComparison.OrdinalIgnoreCase)
            || driver.Contains("generic", StringComparison.OrdinalIgnoreCase)
            || driver.Contains("text", StringComparison.OrdinalIgnoreCase)
            || driver.Contains("pos", StringComparison.OrdinalIgnoreCase);
    }

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

            PrintQueue? queue = null;
            if (!string.IsNullOrWhiteSpace(configured) && (allowVirtual || !IsVirtualPrinter(configured)))
            {
                try
                {
                    queue = server.GetPrintQueue(configured);
                }
                catch
                {
                    queue = null;
                }
            }

            if (queue is null)
            {
                // Auto-detect a real thermal / hardware printer
                var all = server.GetPrintQueues().ToList();
                queue = all.FirstOrDefault(q => !IsVirtualPrinter(q.Name) && IsThermalPrinter(q))
                     ?? all.FirstOrDefault(q => !IsVirtualPrinter(q.Name));

                if (queue is null && allowVirtual)
                {
                    queue = LocalPrintServer.GetDefaultPrintQueue();
                }

                if (queue is null)
                {
                    return "No receipt printer found — please connect your receipt printer";
                }

                if (!IsVirtualPrinter(queue.Name))
                {
                    AppSettings.Current.ReceiptPrinterName = queue.Name;
                    AppSettings.Current.Save();
                }
            }

            if (IsVirtualPrinter(queue.Name) && !allowVirtual)
            {
                return $"Configured printer \"{queue.Name}\" is a file printer — pick a real thermal printer";
            }

            // For thermal/POS receipt printers, use direct ESC/POS raw raster printing
            // with automatic paper cutting and zero dialogs.
            if (IsThermalPrinter(queue))
            {
                if (TryPrintEscPos(queue.Name, receipt, isDuplicate))
                {
                    return null;
                }
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

    private static bool TryPrintEscPos(string printerName, Receipt receipt, bool isDuplicate)
    {
        try
        {
            var doc = Build(receipt, isDuplicate);
            doc.PageWidth = RollWidth;
            doc.ColumnWidth = RollWidth;
            doc.PageHeight = double.NaN;
            doc.PagePadding = new Thickness(8);
            doc.Background = Brushes.White;
            doc.Foreground = Brushes.Black;

            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            paginator.PageSize = new Size(RollWidth, 10000);
            var page = paginator.GetPage(0);
            if (page?.Visual is null) return false;

            double bottom = 0;
            if (VisualTreeHelper.GetChildrenCount(page.Visual) > 0 &&
                VisualTreeHelper.GetChild(page.Visual, 0) is Visual child)
            {
                var bounds = VisualTreeHelper.GetDescendantBounds(child);
                if (!bounds.IsEmpty && bounds.Height > 0)
                {
                    bottom = bounds.Bottom;
                }
            }

            const double dpi = 203.0;
            const double scale = dpi / 96.0;
            const int pixelWidth = 576; // 80mm roll standard printable dots (72mm)
            var contentHeight = bottom > 0 ? bottom + 16 : 400;
            var pixelHeight = (int)Math.Ceiling(contentHeight * scale);
            if (pixelHeight <= 0) return false;

            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(page.Visual);

            var stride = pixelWidth * 4;
            var pixels = new byte[stride * pixelHeight];
            rtb.CopyPixels(pixels, stride, 0);

            var escPosBytes = BitmapToEscPosRaster(pixels, pixelWidth, pixelHeight);
            return RawPrinterHelper.SendBytes(printerName, escPosBytes);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BitmapToEscPosRaster(byte[] bgraPixels, int width, int height)
    {
        var widthBytes = (width + 7) / 8;
        var list = new List<byte>(height * widthBytes + 32)
        {
            // ESC @ (Initialize printer)
            0x1B, 0x40,
            // GS v 0 0 xL xH yL yH (Print raster bit image)
            0x1D, 0x76, 0x30, 0x00,
            (byte)(widthBytes % 256),
            (byte)(widthBytes / 256),
            (byte)(height % 256),
            (byte)(height / 256)
        };

        var stride = width * 4;
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < widthBytes; x++)
            {
                byte b = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var px = x * 8 + bit;
                    if (px < width)
                    {
                        var idx = rowOffset + px * 4;
                        var blue = bgraPixels[idx];
                        var green = bgraPixels[idx + 1];
                        var red = bgraPixels[idx + 2];
                        var alpha = bgraPixels[idx + 3];

                        var lum = (int)(0.299 * red + 0.587 * green + 0.114 * blue);
                        if (lum < 180 && alpha > 50)
                        {
                            b |= (byte)(0x80 >> bit);
                        }
                    }
                }
                list.Add(b);
            }
        }

        // Feed 5 lines: ESC d 5
        list.Add(0x1B);
        list.Add(0x64);
        list.Add(0x05);

        // Cut paper: GS V 1 (Partial cut)
        list.Add(0x1D);
        list.Add(0x56);
        list.Add(0x01);

        return list.ToArray();
    }

    private static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string pDocName = "Receipt";
            [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile = null;
            [MarshalAs(UnmanagedType.LPStr)] public string pDataType = "RAW";
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static bool SendBytes(string printerName, byte[] bytes)
        {
            if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero)) return false;
            try
            {
                var di = new DOCINFOA();
                if (!StartDocPrinter(hPrinter, 1, di)) return false;
                try
                {
                    if (!StartPagePrinter(hPrinter)) return false;
                    try
                    {
                        var p = Marshal.AllocCoTaskMem(bytes.Length);
                        try
                        {
                            Marshal.Copy(bytes, 0, p, bytes.Length);
                            return WritePrinter(hPrinter, p, bytes.Length, out _);
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(p);
                        }
                    }
                    finally
                    {
                        EndPagePrinter(hPrinter);
                    }
                }
                finally
                {
                    EndDocPrinter(hPrinter);
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }
    }

    /// <summary>
    /// Prints after letting Windows ask which printer, and remembers the answer.
    ///
    /// The fallback for the one case <see cref="PrintSilent"/> refuses to guess at: no printer
    /// chosen yet, or the one Windows would have used writes a file instead of putting ink on
    /// paper. Most machines leave "Microsoft Print to PDF" as the Windows default, so a shop
    /// that has just plugged a thermal printer in would otherwise be told there is nowhere to
    /// print and given no way to say where.
    ///
    /// Asked once. What comes back is saved, so every sale after this one goes straight to the
    /// roll with no dialog — which is the only thing that matters with a customer waiting.
    ///
    /// Returns null when it printed, or when the shop closed the dialog and asked for nothing.
    /// </summary>
    public static string? PrintChosen(Receipt receipt, bool isDuplicate)
    {
        try
        {
            var dialog = new PrintDialog();

            // Opens on the one already chosen, when there is one and it still exists.
            var configured = AppSettings.Current.ReceiptPrinterName;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    using var server = new LocalPrintServer();
                    dialog.PrintQueue = server.GetPrintQueue(configured);
                }
                catch
                {
                    // Unplugged or renamed since. Windows opens on its own default instead.
                }
            }

            if (dialog.ShowDialog() != true) return null;

            if (dialog.PrintQueue?.Name is { Length: > 0 } picked)
            {
                AppSettings.Current.ReceiptPrinterName = picked;
                AppSettings.Current.Save();
            }

            var document = Build(receipt, isDuplicate);
            document.PageWidth = RollWidth;
            document.PageHeight = dialog.PrintableAreaHeight;
            document.ColumnWidth = RollWidth;

            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,
                $"Receipt {receipt.InvoiceNumber}{(isDuplicate ? " (duplicate)" : string.Empty)}");
            return null;
        }
        catch (Exception ex)
        {
            return Loc.T("Could not print: {0}", ex.Message);
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

            // A receipt is thirty-two columns of fixed-width text: the label on the left, the
            // figure padded to the right edge. That only holds in a left-to-right paragraph.
            // Laid out right to left for Arabic, the bidi algorithm moved every Latin run to
            // the other end and the paper came out reading "DH 64.50  المجموع" with the
            // columns gone. The Arabic words still read right to left inside the line, which
            // is correct; it is the column that must not move.
            FlowDirection = FlowDirection.LeftToRight,
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
    /// <summary>
    /// A label on the left, a figure padded to the right edge — the shape of every line on a
    /// receipt.
    ///
    /// The marks around the figure are what keep it together next to an Arabic label. Without
    /// them the bidi algorithm reads "64.50 DH" as two runs either side of the Arabic word and
    /// prints "DH 64.50 المجموع", with the currency on the wrong side of its own number. The
    /// padding is measured on the visible text, not on the marks, or the columns would drift a
    /// character on every translated line.
    /// </summary>
    private static string Pair(string left, string right)
    {
        left = Clip(left, Columns - right.Length - 1);
        var gap = Math.Max(1, Columns - left.Length - right.Length);
        return left + new string(' ', gap) + Loc.Ltr(right);
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

    /// <summary>
    /// Plain digits, no marks: <see cref="Pair"/> adds them once the column width has been
    /// measured, and adding them here as well would make every amount two characters wider
    /// than it looks.
    /// </summary>
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
