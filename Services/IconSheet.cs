using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MarketPos.Views.Admin;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MarketPos.Services;

/// <summary>
/// Renders the back-office sidebar icons to a PNG contact sheet.
///
/// Icon geometry is written by hand as path data, and path data cannot be read. Every
/// judgement about whether a shape says "damage and loss" or just looks like a smudge has to
/// be made by looking at it, at the size it is actually drawn — so this draws the set the way
/// the sidebar does and saves it where it can be opened.
///
/// Run with --icons.
/// </summary>
public static class IconSheet
{
    public static int Write(Application app, string path)
    {
        (string Key, string Label)[] icons =
        [
            ("Nav.Dashboard", "DASHBOARD"),
            ("Nav.SalesHistory", "SALES HISTORY"),
            ("Nav.AddProduct", "ADD PRODUCT"),
            ("Nav.Categories", "CATEGORIES"),
            ("Nav.Inventory", "INVENTORY"),
            ("Nav.Suppliers", "SUPPLIERS"),
            ("Nav.Purchases", "DELIVERIES"),
            ("Nav.Expenses", "EXPENSES"),
            ("Nav.Workers", "WORKERS"),
            ("Nav.Salaries", "PAY"),
            ("Nav.Reports", "REPORTS"),
            ("Nav.Activity", "ACTIVITY LOG"),
            ("Icon.Search", "SEARCH"),
            ("Icon.Lock", "LOCK"),
            ("Icon.Settings", "SETTINGS"),
        ];

        const int columns = 5;
        const int cell = 150;

        var grid = new UniformGrid
        {
            Columns = columns,
            Background = Brushes.White,
            Width = columns * cell,
            Height = (int)Math.Ceiling(icons.Length / (double)columns) * cell,
        };

        foreach (var (key, label) in icons)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Drawn twice: once large enough to judge the shape, once at the 15px the
            // sidebar actually uses, where a fussy detail turns into a grey blob.
            stack.Children.Add(Draw(app, key, 48, 1.9));
            stack.Children.Add(Draw(app, key, 15, 2.1));

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
            });

            grid.Children.Add(stack);
        }

        grid.Measure(new Size(grid.Width, grid.Height));
        grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
        grid.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)grid.Width * 2, (int)grid.Height * 2,
                                            192, 192, PixelFormats.Pbgra32);
        bitmap.Render(grid);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        png.Save(file);

        Console.WriteLine($"{icons.Length} icons written to {path}");

        WriteTypeSheet(app, System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path)!, "type.png"));

        try
        {
            Catalog.Load();   // the shell counts alerts on construction, which reads the shop
            WriteSidebar(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(path)!,
                System.IO.Path.GetFileNameWithoutExtension(path) + "-sidebar.png"));
        }
        catch (Exception error)
        {
            // A diagnostic that cannot draw the sidebar has still drawn the icons; it must
            // not take the process down, or worse, stop on a dialog nobody is there to close.
            Console.WriteLine($"sidebar not drawn: {error.GetType().Name}: {error.Message}");
        }

        return 0;
    }

    /// <summary>
    /// The same screens in each language the shop can be run in, so a translation that runs
    /// off the end of a button is caught here rather than at the counter. French is longer
    /// than English almost everywhere, and Arabic turns the whole layout over.
    /// </summary>
    private static void WriteEveryLanguage(string path)
    {
        foreach (var language in new[] { Models.Language.French, Models.Language.Arabic })
        {
            try
            {
                Services.Loc.Use(language);
                Services.Localizer.Start();
                Services.Loc.RecordMisses = true;
                Console.WriteLine($"  [{language}] current={Services.Loc.Current} "
                                + $"sample=\"{Services.Loc.T("Sales history")}\" "
                                + $"table={Services.Translations.Table.Count}");

                var till = new Views.MainWindow();
                Dialog(till, 1500, System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(path)!,
                    $"till-{Services.Loc.Code(language)}.png"));

                var shell = new Views.AdminWindow();
                shell.ShowWhatThisPersonMaySee();

                // Every page, not just the dashboard: each one writes its own sentences when
                // it loads, and this is where the app tells us which of them it cannot say.
                foreach (var page in Enum.GetValues<Models.AdminPage>())
                    Shot(shell, page, System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(path)!,
                        $"{Services.Loc.Code(language)}-{page}.png"));

                // And the dialogs, which nobody reaches by navigating.
                foreach (var dialog in Dialogs())
                    Services.Localizer.Apply((FrameworkElement)dialog.Content);
            }
            catch (Exception error)
            {
                Console.WriteLine($"{language} not drawn: {error.GetType().Name}: {error.Message}");
            }
        }

        Services.Loc.RecordMisses = false;
        Services.Loc.Use(Models.Language.English);

        if (Services.Loc.Misses.Count == 0) return;

        var report = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path)!, "untranslated.txt");
        File.WriteAllLines(report, Services.Loc.Misses);
        Console.WriteLine($"  {Services.Loc.Misses.Count} phrases still untranslated -> {report}");
    }

    /// <summary>
    /// The real back-office sidebar, as it is actually built and styled. The contact sheet
    /// says what the geometry looks like; this says what the shop sees.
    /// </summary>
    private static void WriteSidebar(string path)
    {
        Services.Session.UnlockAsOwner();

        var shell = new Views.AdminWindow();
        shell.ShowWhatThisPersonMaySee();

        // Also draw each page, so a layout can be judged before it reaches the counter.
        foreach (var page in Enum.GetValues<Models.AdminPage>())
        {
            try
            {
                Shot(shell, page, System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(path)!, $"page-{page}.png"));
            }
            catch (Exception error)
            {
                Console.WriteLine($"{page} not drawn: {error.GetType().Name}: {error.Message}");
            }
        }

        // Rendered as a whole window rather than by pulling the sidebar out of it: the nav
        // panel has no visual parent until the window has been laid out, and reparenting an
        // unarranged element is how the first attempt at this returned nothing at all.
        var root = (FrameworkElement)shell.Content;
        root.Measure(new Size(1280, 860));
        root.Arrange(new Rect(0, 0, 1280, 860));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(2560, 1720, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);

        // Only the sidebar is worth looking at here.
        var sidebar = new CroppedBitmap(bitmap, new Int32Rect(0, 0, 540, 1720));

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(sidebar));
        using var file = File.Create(path);
        png.Save(file);

        Console.WriteLine($"sidebar written to {path}");

        // The dialogs too. A window that only opens on a click is the easiest thing to ship
        // with a field pushed off the edge.
        Dialog(new PurchaseWindow(null), 980,
               System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "dialog-Purchase.png"));
        // The till with a basket in it, so the totals panel is drawn with figures in it. An
        // empty cart hides the whole right-hand column.
        try
        {
            var selling = new Views.MainWindow();
            foreach (var product in Services.Catalog.Products.Take(3))
                selling.Vm.AddProductCommand.Execute(product);

            Dialog(selling, 1500,
                   System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "till-cart.png"));
        }
        catch (Exception error)
        {
            Console.WriteLine($"cart not drawn: {error.GetType().Name}: {error.Message}");
        }

        // The till itself, which is the screen that runs all day.
        Dialog(new Views.MainWindow(), 1500,
               System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "till.png"));

        // The price card, which only exists after a scan. Photographed with a real barcode
        // out of the shop's own catalogue, so a shop with nothing in it draws the miss.
        var till = new Views.MainWindow();
        till.Vm.IsPriceCheck = true;
        till.Vm.CheckPrice(Data.StockRepository.List().FirstOrDefault()?.Barcode ?? "6111234500042");
        Dialog(till, 1500,
               System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "till-pricecheck.png"));

        Dialog(new Views.SettingsWindow(), 500,
               System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "dialog-Settings.png"));

        Dialog(new SupplierWindow(null), 940,
               System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "dialog-Supplier.png"));
        // Add product opens on its list; the form is the second state and the one worth
        // looking at, so it is shown before the photograph is taken.
        try
        {
            var add = new AddProductPage();
            add.Attach(new ViewModels.AdminContext(), shell);
            add.Refresh();
            add.ShowAddForm();

            Pane(add, 1330, 820, System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(path)!, "page-AddProduct-form.png"));
        }
        catch (Exception error)
        {
            Console.WriteLine($"add form not drawn: {error.GetType().Name}: {error.Message}");
        }

        // The product form, on a real product, so the photo well is drawn with something in it.
        Dialog(new ProductWindow(Data.StockRepository.List().FirstOrDefault()), 940,
               System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "dialog-Product.png"));

        Dialog(new CategoryWindow(null), 460,
               System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "dialog-Category.png"));

        WriteEveryLanguage(path);
    }

    /// <summary>
    /// One of each dialog, built but not drawn. Only their words are wanted here — a dialog
    /// writes its heading and its explanation in its constructor, and that is what has to be
    /// caught before somebody meets it in French.
    /// </summary>
    private static IEnumerable<Window> Dialogs()
    {
        var makers = new Func<Window>[]
        {
            () => new PurchaseWindow(null),
            () => new SupplierWindow(null),
            () => new CategoryWindow(null),
            () => new ProductWindow(null),
            () => new ExpenseWindow(null),
            () => new WorkerWindow(null),
            () => new Views.SettingsWindow(),
            () => new Views.ReprintWindow(),
            () => new Views.DiscountWindow(100m, Models.DiscountKind.None, 0m),
            () => new Views.StaffSignInWindow(),
            () => new Views.AdminLoginWindow(),
        };

        foreach (var make in makers)
        {
            Window? window = null;
            try { window = make(); }
            catch (Exception error)
            {
                Console.WriteLine($"  dialog skipped: {error.GetType().Name}: {error.Message}");
            }
            if (window is not null) yield return window;
        }
    }

    private static void Dialog(Window window, int width, string path)
    {
        var root = (FrameworkElement)window.Content;

        // Nothing raises Loaded in a window that is never shown, so the class handler that
        // translates the running app does not fire here. Walk it instead.
        Services.Localizer.Apply(root);

        // And the app is turned over for Arabic by metadata on Window, which a window that is
        // never shown does not go through either. Without this the Arabic screenshots came out
        // reading right but laid out left, which is the half that hides the real problems.
        root.FlowDirection = Services.Loc.IsRightToLeft
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // Measured against a tall box first: a window that sizes to its content reports the
        // height it wants, and one that fills the screen reports far less than it uses.
        root.Measure(new Size(width, 2000));
        var height = Math.Max(root.DesiredSize.Height, window is Views.MainWindow ? 900 : 0);
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, (int)root.ActualHeight, 96, 96,
                                            PixelFormats.Pbgra32);
        bitmap.Render(root);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);

        Console.WriteLine($"  {System.IO.Path.GetFileName(path)}");
    }

    /// <summary>Renders one back-office page at the size the shop's screen gives it.</summary>
    /// <summary>
    /// Renders one page on its own, at the size the content pane gives it. For the states a
    /// page only reaches after somebody has pressed something — where the shell's own
    /// navigation cannot take you.
    /// </summary>
    private static void Pane(FrameworkElement root, int width, int height, string path)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);

        Console.WriteLine($"  {System.IO.Path.GetFileName(path)}");
    }

    private static void Shot(Views.AdminWindow shell, Models.AdminPage page, string path)
    {
        shell.GoTo(page);

        var root = (FrameworkElement)shell.Content;
        Services.Localizer.Apply(root);
        root.FlowDirection = Services.Loc.IsRightToLeft
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        root.Measure(new Size(1600, 900));
        root.Arrange(new Rect(0, 0, 1600, 900));
        root.UpdateLayout();
        Services.Localizer.Apply(root);   // again: the page's own labels exist only now
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);

        Console.WriteLine($"  {page} -> {System.IO.Path.GetFileName(path)}");
    }

    /// <summary>
    /// Every weight of the brand's type, drawn from the embedded files.
    ///
    /// A font that fails to load does not raise anything — WPF quietly falls back to the
    /// system face and the app just looks slightly wrong forever. The only way to know the
    /// four weights actually resolved is to draw them and look.
    /// </summary>
    public static void WriteTypeSheet(Application app, string path)
    {
        var stack = new StackPanel { Background = Brushes.White, Width = 900, Margin = new Thickness(40) };

        foreach (var (family, weights) in new (string, FontWeight[])[]
                 {
                     ("Nunito", [FontWeights.SemiBold, FontWeights.Bold, FontWeights.ExtraBold]),
                     ("Inter", [FontWeights.Regular, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold]),
                 })
        {
            var face = new FontFamily(new Uri("pack://application:,,,/"), $"./Assets/Fonts/#{family}");

            stack.Children.Add(new TextBlock
            {
                Text = family,
                FontFamily = face,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 24, 0, 12),
            });

            foreach (var weight in weights)
            {
                stack.Children.Add(new TextBlock
                {
                    // Digits and the awkward letters: money is what this app is mostly made of,
                    // and 0/O and 1/l are where a till font earns its place.
                    Text = $"{weight}  —  1234567890  0O 1lI  {AppSettings.Current.Currency}  Youssef",
                    FontFamily = face,
                    FontWeight = weight,
                    FontSize = 21,
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }
        }

        stack.Measure(new Size(900, 2000));
        stack.Arrange(new Rect(0, 0, 900, stack.DesiredSize.Height));
        stack.UpdateLayout();

        var bitmap = new RenderTargetBitmap(900, (int)stack.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stack);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);

        Console.WriteLine($"  type sheet -> {System.IO.Path.GetFileName(path)}");
    }

    private static System.Windows.Shapes.Path Draw(Application app, string key, double size, double weight) =>
        new()
        {
            Data = (Geometry)app.FindResource(key),
            Stroke = Brushes.Black,
            StrokeThickness = weight,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = null,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
}
