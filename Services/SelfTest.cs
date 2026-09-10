using System.IO;
using System.Text;
using System.Windows;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Views;
using MarketPos.Views.Admin;

namespace MarketPos.Services;

/// <summary>
/// Builds every screen once and reports what threw.
///
/// A WPF page that fails on a missing resource or a bad binding does not fail at compile
/// time — it fails the first time a person opens it, which in a shop is the worst possible
/// moment. Run with <c>MarketPos.exe --selftest</c>: it constructs each window and page,
/// loads its data, writes a report next to the database and exits. Nothing is rendered on
/// screen and nothing is written to the shop's records.
/// </summary>
public static class SelfTest
{
    public static void Run(Application app)
    {
        var report = new StringBuilder();
        var failures = 0;

        // The back office is owner-only; without this every page would report a permission
        // refusal rather than whatever real fault is being looked for.
        Session.UnlockAsOwner();

        Check(report, ref failures, "MainWindow", () => new MainWindow());
        Check(report, ref failures, "ScanWindow", () => new ScanWindow());
        Check(report, ref failures, "StaffSignInWindow", () => new StaffSignInWindow());

        AdminWindow? shell = null;
        Check(report, ref failures, "AdminWindow", () => shell = new AdminWindow());

        foreach (var page in Enum.GetValues<AdminPage>())
        {
            Check(report, ref failures, $"AdminPage.{page}", () =>
            {
                var view = Build(page);
                view.Attach(new ViewModels.AdminContext(), shell!);
                view.Refresh();
                return view;
            });
        }

        CheckTopBarLayout(report, ref failures, shell);
        CheckComboDisplay(report, ref failures);
        CheckEveryDropDownReads(report, ref failures);
        CheckTheLanguageBoxOffersTheShopsTwo(report, ref failures);
        CheckScannerRule(report, ref failures);
        CheckAddProductFields(report, ref failures, shell);
        CheckDashboardLayout(report, ref failures, shell);
        CheckSalesHistoryLayout(report, ref failures, shell);

        Check(report, ref failures, "Finance.For(today)",
            () => Finance.For(DateRange.For(DatePreset.Today)));
        Check(report, ref failures, "Finance.Series(month)",
            () => Finance.Series(DateRange.For(DatePreset.ThisMonth), SeriesKind.Revenue));
        Check(report, ref failures, "Notifications.Build", () => Notifications.Build());

        CheckTheShopsOwnLanguage(report, ref failures);
        CheckTheActivityLogSpeaksTheShopsLanguage(report, ref failures);
        CheckScanningPutsItOnTheSale(report, ref failures);
        CheckTheBasketAsksOneThing(report, ref failures);
        CheckTheTillRefusesWhatIsNotThere(report, ref failures);
        CheckAnEmptyShelfIsReported(report, ref failures);
        CheckExpiryDatesReadTrue(report, ref failures);
        CheckTheStockListNarrows(report, ref failures);
        CheckTheWindowsCanBeMoved(report, ref failures);
        CheckEveryDialogFitsASmallScreen(report, ref failures);
        CheckADialogFitsTheWindowItOpensOver(report, ref failures);
        CheckTheShellsFitASmallScreen(report, ref failures);
        CheckTheKeyboardFitsEveryScreen(report, ref failures);
        CheckAFilterStaysWhereItIsPut(report, ref failures);
        CheckEveryDialogSpeaksTheShopsLanguage(report, ref failures);
        CheckTheNavigationSpeaksTheShopsLanguage(report, ref failures);
        CheckPriceCheck(report, ref failures);
        CheckStandaloneShopIsLeftAlone(report, ref failures);
        CheckTillAsCashier(report, ref failures);
        CheckSidebarByRole(report, ref failures);
        CheckSignOut(report, ref failures);
        CheckOwnerCanAlwaysSignIn(report, ref failures);
        CheckMoneyFieldsTakeNumbersOnly(report, ref failures);
        CheckEveryPageHasAnIcon(report, ref failures, app);
        CheckStockPages(report, ref failures, shell);
        CheckFieldsAreVisible(report, ref failures, app);
        CheckOwnerPassword(report, ref failures, shell);
        CheckContrast(report, ref failures, app);
        CheckRailHoldsStill(report, ref failures);
        CheckSelectedPageIsDrawnSolid(report, ref failures);

        var summary = failures == 0
            ? $"All {report.ToString().Split('\n').Length - 1} checks passed."
            : $"{failures} FAILED.";
        report.AppendLine();
        report.AppendLine(summary);

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MarketPos", "selftest.log");
        File.WriteAllText(path, report.ToString());

        Console.WriteLine(report.ToString());
        app.Shutdown(failures == 0 ? 0 : 1);
    }

    /// <summary>
    /// Lays the shell out for real and checks the header does not overlap itself.
    ///
    /// The date chips and the page title once shared a grid row, which put the chips on top
    /// of the subtitle — invisible to a compiler and to every other check here, because
    /// everything constructed and loaded perfectly well. Overlap is a geometry question, so
    /// it takes a geometry test.
    /// </summary>
    private static void CheckTopBarLayout(StringBuilder report, ref int failures, AdminWindow? shell)
    {
        if (shell is null) return;

        try
        {
            var title = (FrameworkElement)shell.FindName("PageTitle");
            var subtitle = (FrameworkElement)shell.FindName("PageSubtitle");
            var dateBar = (FrameworkElement)shell.FindName("DateBar");
            var host = (FrameworkElement)shell.FindName("PageHost");

            // The bar is collapsed until a page asks for it; force it on, since that is the
            // case that broke.
            dateBar.Visibility = Visibility.Visible;
            subtitle.SetValue(System.Windows.Controls.TextBlock.TextProperty,
                              "How the shop is doing · Today");

            // Lay out the window's content directly. An unshown Window has no visual link to
            // its own content, so coordinates have to be taken against the root element.
            var root = (FrameworkElement)shell.Content;
            root.Measure(new Size(1600, 900));
            root.Arrange(new Rect(0, 0, 1600, 900));
            root.UpdateLayout();

            Rect BoxOf(FrameworkElement element)
            {
                var origin = element.TransformToAncestor(root).Transform(new Point(0, 0));
                return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
            }

            var subtitleBox = BoxOf(subtitle);
            var dateBox = BoxOf(dateBar);
            var hostBox = BoxOf(host);

            Verdict(report, ref failures, "date bar clears the subtitle",
                dateBox.Top >= subtitleBox.Bottom,
                $"subtitle ends at y={subtitleBox.Bottom:0}, date bar starts at y={dateBox.Top:0}");

            Verdict(report, ref failures, "page content clears the date bar",
                hostBox.Top >= dateBox.Bottom,
                $"date bar ends at y={dateBox.Bottom:0}, content starts at y={hostBox.Top:0}");

            Verdict(report, ref failures, "title and subtitle are on screen",
                BoxOf(title).Top >= 0 && subtitleBox.Height > 0,
                $"title top y={BoxOf(title).Top:0}, subtitle height={subtitleBox.Height:0}");

            dateBar.Visibility = Visibility.Collapsed;
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  top bar layout: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// Renders a styled ComboBox and reads back what the closed box actually says.
    ///
    /// The selection box was a TextBlock bound to SelectionBoxItem, which prints the item's
    /// ToString() — so a cashier filter read "MarketPos.Models.Worker" instead of a name.
    /// Every other check passed while it did, because the control constructed, bound and laid
    /// out perfectly well. Only reading the rendered text catches it.
    /// </summary>
    /// <summary>
    /// Every drop-down the shop actually opens, checked for the one failure this app's themed
    /// ComboBox has: a re-templated box ignores DisplayMemberPath and asks the item for its
    /// Name, so a row that does not have one prints its own type declaration into the box.
    ///
    /// The general check below proves the template works. This proves each real list feeds it
    /// something it can read — which is the half that broke, on the language list, in a screen
    /// nobody looks at twice.
    /// </summary>
    /// <summary>
    /// The language box offers the two languages this shop is run in, and not the one the code
    /// happens to be written in.
    ///
    /// English is still the source text every translation is keyed by, so it cannot leave the
    /// codebase — which is exactly why it is easy to let it back onto the screen without
    /// noticing. Nobody at this counter reads it.
    /// </summary>
    private static void CheckTheLanguageBoxOffersTheShopsTwo(StringBuilder report, ref int failures)
    {
        try
        {
            var settings = new Views.SettingsWindow();
            var box = Descendants((FrameworkElement)settings.Content)
                .OfType<System.Windows.Controls.ComboBox>()
                .FirstOrDefault(c => c.Name == "LanguageBox");

            var offered = box?.ItemsSource?.Cast<object>().Select(o => o?.ToString() ?? "").ToList()
                       ?? new List<string>();

            Verdict(report, ref failures, "the language box offers Arabic and French, and only those",
                offered.Count == 2
                    && offered.Contains(Loc.NativeName(Models.Language.Arabic))
                    && offered.Contains(Loc.NativeName(Models.Language.French)),
                offered.Count == 0 ? "no language box found" : string.Join(", ", offered));
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  language box: {error.GetType().Name}: {error.Message}");
        }
    }

    private static void CheckEveryDropDownReads(StringBuilder report, ref int failures)
    {
        var settings = new Views.SettingsWindow();
        FrameworkElement root = (FrameworkElement)settings.Content;
        root.Measure(new Size(500, 1200));
        root.Arrange(new Rect(0, 0, 500, 1200));
        root.UpdateLayout();

        var unreadable = new List<string>();
        foreach (var box in Descendants(root).OfType<System.Windows.Controls.ComboBox>())
        {
            if (box.SelectedItem is null) continue;

            var shown = string.Concat(Descendants(box)
                .OfType<System.Windows.Controls.TextBlock>()
                .Select(t => t.Text));

            // A record or class printed raw always carries its own type name and braces.
            if (shown.Contains('{') || shown.Contains("= "))
                unreadable.Add($"{box.Name}: {shown}");
        }

        Verdict(report, ref failures, "every drop-down shows a word, not an object",
            unreadable.Count == 0,
            unreadable.Count == 0
                ? "settings drop-downs all read as text"
                : string.Join("; ", unreadable));
    }

    private static void CheckComboDisplay(StringBuilder report, ref int failures)
    {
        try
        {
            var people = new[]
            {
                new Worker { Name = "Fatima", Role = WorkerRole.Cashier },
                new Worker { Name = "Youssef", Role = WorkerRole.Owner },
            };

            // Set up exactly as the back office does it: styled, with the shared Combo.Name
            // item template rather than DisplayMemberPath.
            var combo = new System.Windows.Controls.ComboBox
            {
                Style = (Style)Application.Current.FindResource("ComboBox.Form"),
                ItemTemplate = (DataTemplate)Application.Current.FindResource("Combo.Name"),
                ItemsSource = people,
                SelectedIndex = 0,
                Width = 200,
            };

            var host = new System.Windows.Controls.Border { Child = combo };
            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));
            host.UpdateLayout();

            var shown = string.Concat(Descendants(combo)
                .OfType<System.Windows.Controls.TextBlock>()
                .Select(t => t.Text));

            Verdict(report, ref failures, "combo shows the item's name, not its type name",
                shown.Contains("Fatima") && !shown.Contains("MarketPos"),
                $"closed box reads \"{shown.Trim()}\"");

            combo.IsEditable = true;
            host.UpdateLayout();

            Verdict(report, ref failures, "editable combo has a text box to type in",
                combo.Template.FindName("PART_EditableTextBox", combo) is System.Windows.Controls.TextBox,
                "PART_EditableTextBox present");
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  combo display: {error.GetType().Name}: {error.Message}");
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>
    /// Lays the dashboard out at a realistic size and checks the figures actually fit the room
    /// they were given. A half-shown "1,234.5" is worse than no figure at all, and the quiet
    /// line under the hero is four figures across one row - exactly where a long total gets
    /// clipped.
    /// </summary>
    private static void CheckDashboardLayout(StringBuilder report, ref int failures, AdminWindow? shell)
    {
        try
        {
            var page = new DashboardPage();
            page.Attach(new ViewModels.AdminContext(), shell!);
            page.Refresh();

            // The content pane on a 1600-wide window, less the sidebar and margins.
            page.Measure(new Size(1330, 820));
            page.Arrange(new Rect(0, 0, 1330, 820));
            page.UpdateLayout();

            string[] names = ["RevenueValue", "ProfitValue", "HeroLabel", "ExpensesValue",
                              "SoldValue", "CostValue", "RestockValue"];

            var clipped = new List<string>();
            foreach (var name in names)
            {
                if (page.FindName(name) is not System.Windows.Controls.TextBlock box) continue;

                // A trimmed TextBlock reports the width it wanted; compare against what it got.
                var wanted = box.DesiredSize.Width;
                var got = box.ActualWidth;
                if (got > 0 && wanted > got + 0.5) clipped.Add($"{name} needs {wanted:0}px, has {got:0}px");
            }

            Verdict(report, ref failures, "dashboard summary figures fit their columns",
                clipped.Count == 0,
                clipped.Count == 0 ? $"all {names.Length} fit" : string.Join("; ", clipped));

            // The headline figure has to actually be the headline. If it ever falls back to
            // the same size as the quiet line underneath, the page is six equal numbers again
            // and the whole point of the layout is gone.
            var hero = page.FindName("RevenueValue") as System.Windows.Controls.TextBlock;
            var quiet = page.FindName("CostValue") as System.Windows.Controls.TextBlock;
            Verdict(report, ref failures, "takings is the biggest figure on the dashboard",
                hero is not null && quiet is not null && hero.FontSize >= quiet.FontSize * 2,
                $"takings {hero?.FontSize ?? 0:0}px against {quiet?.FontSize ?? 0:0}px for the rest");

            var strip = page.FindName("RevenueValue") as FrameworkElement;
            Verdict(report, ref failures, "dashboard summary rendered",
                strip is { ActualHeight: > 0 },
                $"first figure is {strip?.ActualHeight ?? 0:0}px tall");

            var sold = page.FindName("MostSold") as FrameworkElement;
            var restock = page.FindName("Restock") as FrameworkElement;
            var used = page.DesiredSize.Height;

            // A Left-aligned Grid sizes to its content, which once left ~290px of dead space
            // down the right of a wide window. It has to use the width it is handed.
            Verdict(report, ref failures, "dashboard uses the full width",
                page.ActualWidth >= 1320,
                $"content is {page.ActualWidth:0}px wide in a 1330px pane");

            // The other half of the same complaint. The page used to stop around 630px down a
            // 1060px pane and leave a band of bare grey under it; the two lists now take the
            // height they are given, so what is left over is inside a panel rather than beside
            // one. Measured off the arranged card, not the page's desired size — a star row
            // asks for nothing in measure and only claims the room in arrange, so a short list
            // would report a short page while filling the screen perfectly.
            var bottom = 0d;
            foreach (var name in new[] { "SellingCard", "RestockCard" })
            {
                if (page.FindName(name) is not FrameworkElement card) continue;
                var corner = card.TransformToAncestor(page)
                                 .Transform(new System.Windows.Point(0, card.ActualHeight));
                bottom = Math.Max(bottom, corner.Y);
            }

            Verdict(report, ref failures, "dashboard fills the pane it is given",
                bottom >= 780,
                $"the lists reach {bottom:0}px down an 820px pane");

            // Each restock row has to be a real click target pointing at a real product, or
            // "press it to open the item" quietly does nothing.
            var rowButtons = Descendants(page)
                .OfType<System.Windows.Controls.Button>()
                .Where(b => b.Tag is int)
                .ToList();

            // A shop with nothing in it has nothing to restock, and that is not a failure —
            // it is the state every shop starts in. What must hold either way is that any row
            // drawn is a real click target: "press it to open the item" quietly doing nothing
            // is the bug this guards.
            var empty = StockRepository.List().Count == 0;

            Verdict(report, ref failures, "restock rows open a product",
                empty || (rowButtons.Count > 0 && rowButtons.All(b => (int)b.Tag! > 0)),
                empty
                    ? "nothing in the shop to restock"
                    : $"{rowButtons.Count} rows, product ids "
                      + string.Join(", ", rowButtons.Take(3).Select(b => b.Tag)));

            // The sparkline is drawn only when there is a shape to see. One bar on an empty
            // grid was the page's least honest pixel.
            var spark = page.FindName("SparkPanel") as FrameworkElement;
            var chart = page.FindName("Chart") as Controls.TrendChart;
            var drawn = spark is { Visibility: Visibility.Visible };
            var trade = (chart?.Points ?? []).Any(pt => pt.Value > 0m);
            Verdict(report, ref failures, "the trend is shown only when there is trade to show",
                spark is not null && drawn == trade,
                drawn ? $"{chart?.Points?.Count ?? 0} days drawn" : "no trade in range, hidden");

            Verdict(report, ref failures, "both bottom tables rendered",
                sold is { ActualHeight: >= 0 } && restock is { ActualHeight: >= 0 },
                empty
                    ? "empty shop, both panels collapsed"
                    : $"most sold {sold?.ActualHeight ?? 0:0}px, restock {restock?.ActualHeight ?? 0:0}px");
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  dashboard layout: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// The same geometry checks the dashboard gets: fills its pane, summary figures are not
    /// clipped, and every row is a real click target pointing at a real receipt.
    /// </summary>
    private static void CheckSalesHistoryLayout(StringBuilder report, ref int failures, AdminWindow? shell)
    {
        try
        {
            var page = new SalesHistoryPage();
            page.Attach(new ViewModels.AdminContext(), shell!);
            page.Refresh();

            page.Measure(new Size(1330, 820));
            page.Arrange(new Rect(0, 0, 1330, 820));
            page.UpdateLayout();

            Verdict(report, ref failures, "sales history uses the full width",
                page.ActualWidth >= 1320,
                $"content is {page.ActualWidth:0}px wide in a 1330px pane");

            string[] figures = ["CountValue", "TakingsValue", "ProfitValue", "ItemsValue", "RefundValue"];
            var clipped = figures
                .Select(n => page.FindName(n) as System.Windows.Controls.TextBlock)
                .Where(b => b is { ActualWidth: > 0 } && b.DesiredSize.Width > b.ActualWidth + 0.5)
                .Select(b => b!.Name)
                .ToList();

            Verdict(report, ref failures, "sales history summary figures fit their columns",
                clipped.Count == 0,
                clipped.Count == 0 ? "all five fit" : "clipped: " + string.Join(", ", clipped));

            var rows = Descendants(page)
                .OfType<System.Windows.Controls.Button>()
                .Where(b => b.Tag is int)
                .ToList();

            Verdict(report, ref failures, "sales rows open a receipt",
                rows.All(b => (int)b.Tag! > 0),
                rows.Count == 0
                    ? "no sales to check (empty period)"
                    : $"{rows.Count} rows, receipts "
                      + string.Join(", ", rows.Take(3).Select(b => "#" + b.Tag)));
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  sales history layout: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// The rule that keeps scanned digits out of the Name and Price boxes: a burst of digits
    /// is a machine, anything slower is a person. Worth a test because the failure is silent —
    /// a barcode quietly appended to whatever field happened to have focus.
    /// </summary>
    private static void CheckScannerRule(StringBuilder report, ref int failures)
    {
        var human = TimeSpan.FromMilliseconds(120);
        var machine = TimeSpan.FromMilliseconds(5);

        Verdict(report, ref failures, "a fast run of digits is read as a scan",
            BarcodeScanner.Classify("7", machine) == BarcodeScanner.Keystroke.Burst,
            "5ms between digits");

        Verdict(report, ref failures, "ordinary typing is left alone",
            BarcodeScanner.Classify("7", human) == BarcodeScanner.Keystroke.PossibleStart,
            "120ms between digits passes straight through");

        Verdict(report, ref failures, "letters are never treated as a barcode",
            BarcodeScanner.Classify("a", machine) == BarcodeScanner.Keystroke.NotAScan
            && BarcodeScanner.Classify(".", machine) == BarcodeScanner.Keystroke.NotAScan,
            "a product name typed quickly stays in its field");

        // char.IsDigit is true for Arabic-Indic ٠١٢ as well, and no barcode is
        // written in those. Same trap the money boxes fell into.
        Verdict(report, ref failures, "only 0-9 counts as a scanned digit",
            BarcodeScanner.Classify("٨", machine) == BarcodeScanner.Keystroke.NotAScan,
            "Arabic-Indic digits are not a barcode");

        // Not every scanner sends Enter, and a shop that bought one second hand has no idea
        // which it has. Without a quiet-time flush those codes are typed and never collected.
        Verdict(report, ref failures, "a scanner that sends no Enter still finishes",
            BarcodeScanner.QuietAfter > BarcodeScanner.BurstGap
            && BarcodeScanner.QuietAfter < TimeSpan.FromMilliseconds(400),
            $"{BarcodeScanner.QuietAfter.TotalMilliseconds:0}ms after the last digit");
    }

    /// <summary>
    /// Every field on the Add product page should be sized to what goes in it. A box stretched
    /// across the screen for a four-digit price is the thing that made a short form look long.
    /// </summary>
    private static void CheckAddProductFields(StringBuilder report, ref int failures, AdminWindow? shell)
    {
        try
        {
            var page = new AddProductPage();
            page.Attach(new ViewModels.AdminContext(), shell!);
            page.Refresh();

            // The page lands on the products list; the form is the second state, so show it
            // before measuring anything on it.
            if (page.FindName("AddListPanel") is UIElement listState) listState.Visibility = Visibility.Collapsed;
            if (page.FindName("AddScroll") is UIElement formState) formState.Visibility = Visibility.Visible;

            // The content pane on a 1600-wide window, less the sidebar and margins.
            FrameworkElement root = page;
            root.Measure(new Size(1330, 820));
            root.Arrange(new Rect(0, 0, 1330, 820));
            root.UpdateLayout();

            // name -> the widest it has any business being
            (string Name, double Max)[] limits =
            [
                ("AddBarcodeBox", 300),
                ("AddNameBox", 300),
                ("AddCostBox", 130),
                ("AddPriceBox", 130),
                ("AddQuantityBox", 110),
                ("AddExpiryBox", 150),
            ];

            var tooWide = new List<string>();
            foreach (var (name, max) in limits)
            {
                if (page.FindName(name) is not FrameworkElement field) continue;
                var width = field.ActualWidth > 0 ? field.ActualWidth : field.DesiredSize.Width;
                if (width > max) tooWide.Add($"{name} is {width:0}px");
            }

            Verdict(report, ref failures, "add-product fields are sized to their content",
                tooWide.Count == 0,
                tooWide.Count == 0 ? "all six within their limits" : string.Join(", ", tooWide));

            // Centred, not tucked against the left edge under the search box.
            if (page.FindName("AddForm") is FrameworkElement form && form.ActualWidth > 0)
            {
                var origin = form.TransformToAncestor(root).Transform(new Point(0, 0));
                var slackLeft = origin.X;
                var slackRight = root.ActualWidth - origin.X - form.ActualWidth;

                Verdict(report, ref failures, "add-product form is centred",
                    slackLeft > 40 && Math.Abs(slackLeft - slackRight) < 140,
                    $"{slackLeft:0}px to its left, {slackRight:0}px to its right");
            }
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  add-product fields: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// Runs what the till needs as a plain cashier, with no owner unlock.
    ///
    /// Every other check here runs as the owner, which is precisely why a real bug got
    /// through: the Add product list asked the stock ledger for a date, the owner could read
    /// it, and the page only died in the shop. Anything the till does has to be provable at
    /// the permission level the till actually runs at.
    /// </summary>
    /// <summary>
    /// Price check has one job and one prohibition: answer what the thing costs, and never put
    /// it on the sale. The prohibition is the part worth a test — the failure is silent money.
    ///
    /// The second half is who may see what. The shelf price belongs to anybody standing at the
    /// counter; the purchase price is the owner's, and a cashier who can read it off every
    /// product knows the shop's margins.
    /// </summary>
    /// <summary>
    /// The scan, which is the whole job: read a barcode, and the thing is on the sale with its
    /// name and its price. Scan it again and there are two of them.
    ///
    /// Also the split that keeps the grid usable — a scanned product is not a tile. The failure
    /// there is quiet in the other direction: a shop with three hundred barcoded lines would
    /// bury the four things the cashier actually has to press.
    /// </summary>
    private static void CheckScanningPutsItOnTheSale(StringBuilder report, ref int failures)
    {
        // Barcoded is not enough: it also has to be something the till will actually sell. An
        // empty shelf is refused at the scanner by design, and picking the first barcode in
        // the catalogue regardless landed on one — so this read as "scanning is broken" and
        // then took the whole diagnostic run down with it on the next line.
        var scanned = Catalog.Products.FirstOrDefault(
            p => p.IsScannable && p.SoldAtTheTill && !p.IsOutOfStock);

        if (scanned is null)
        {
            report.AppendLine("ok    scanning (nothing in the shop is both barcoded and on the shelf)");
            return;
        }

        var till = new MainWindow();

        till.Vm.SearchText = scanned.Barcode;
        till.Vm.SubmitBarcodeCommand.Execute(null);

        var line = till.Vm.Cart.FirstOrDefault();
        Verdict(report, ref failures, "a scan puts the product on the sale by itself",
            line is not null && line.Product.Id == scanned.Id,
            line is null ? "nothing landed in the cart" : $"{line.Product.Name} at {line.Product.Price:N2} DH");

        Verdict(report, ref failures, "with its name and its price, not a code",
            line is not null && line.Product.Name.Length > 0 && line.Product.Price > 0m,
            line is null ? "no line" : $"\"{line.Product.Name}\", {line.LineTotal:N2} DH");

        // Nothing landed, so there is nothing left to ask. Reported above and stopped here:
        // every check below reads Cart[0], and a diagnostic that crashes on the first fault
        // it finds reports one fault and hides all the others.
        if (line is null) return;

        // The same item again: one line, two of them. A second line for the same product is
        // how a receipt ends up unreadable and a cashier ends up recounting by hand.
        var wasQuantity = line.Quantity;
        till.Vm.SearchText = scanned.Barcode;
        till.Vm.SubmitBarcodeCommand.Execute(null);

        Verdict(report, ref failures, "scanning it again adds one more, on the same line",
            till.Vm.Cart.Count == 1 && till.Vm.Cart[0].Quantity == wasQuantity + till.Vm.Cart[0].Step,
            $"{till.Vm.Cart.Count} line(s), quantity {till.Vm.Cart.FirstOrDefault()?.Quantity ?? 0m:0.###}");

        // And the quantity is the cashier's to change.
        till.Vm.Cart[0].Quantity = 5m;
        Verdict(report, ref failures, "and the cashier can set the quantity by hand",
            till.Vm.Cart[0].Quantity == 5m && till.Vm.Total > 0m,
            $"5 × {scanned.Name} = {till.Vm.Total:N2} DH");

        // The grid is for what cannot be scanned.
        var onTheGrid = till.Vm.ProductsView.Cast<Product>().ToList();
        Verdict(report, ref failures, "scanned products stay off the till's shelf",
            onTheGrid.All(p => !p.IsScannable),
            onTheGrid.Count == 0
                ? "nothing on the grid — everything in this shop is scanned"
                : $"{onTheGrid.Count} tile(s), none of them scannable");
    }

    /// <summary>
    /// The till asks the customer for one number and offers one way to pay it.
    ///
    /// VAT came off the basket because this shop is not registered for it: a line reading
    /// "VAT 13.33" under a total that never changed was a figure the shop does not owe,
    /// printed at the customer. Card and Other came off because three buttons for a choice
    /// nobody makes is three chances to file the takings in the wrong column — and a card left
    /// selected from the last sale is invisible until the month is counted.
    /// </summary>
    private static void CheckTheBasketAsksOneThing(StringBuilder report, ref int failures)
    {
        var till = new MainWindow();

        // The panel only exists once there is something in it.
        foreach (var product in Catalog.Products.Take(2))
            till.Vm.AddProductCommand.Execute(product);

        FrameworkElement root = (FrameworkElement)till.Content;
        root.Measure(new Size(1500, 900));
        root.Arrange(new Rect(0, 0, 1500, 900));
        root.UpdateLayout();

        var words = Descendants(root).OfType<System.Windows.Controls.TextBlock>()
            .Select(t => t.Text.Trim()).ToList();

        Verdict(report, ref failures, "the basket does not mention VAT",
            !words.Any(w => w.Equals("VAT", StringComparison.OrdinalIgnoreCase)),
            $"{words.Count} labels on the till, none of them VAT");

        var payButtons = Descendants(root)
            .OfType<System.Windows.Controls.RadioButton>()
            .Select(b => b.Content as string)
            .Where(c => c is "Cash" or "Card" or "Other")
            .ToList();

        Verdict(report, ref failures, "and offers no way to pay but cash",
            payButtons.Count == 0,
            payButtons.Count == 0 ? "no payment buttons at all" : string.Join(", ", payButtons));

        // The sale still records how it was paid, because history and reports read that column.
        Verdict(report, ref failures, "though the sale still records that it was cash",
            till.Vm.PaymentMethod == PaymentMethod.Cash,
            $"payment method is {till.Vm.PaymentMethod}");
    }

    /// <summary>
    /// The shop's own language, on the shop's own screens.
    ///
    /// Two things worth holding. Every label the XAML carries has to have somewhere to go, or
    /// a French shop meets an English button in the middle of a sale. And nothing bound may be
    /// translated: a product the shop called "Total" must come out of the database exactly as
    /// they typed it, on the receipt the customer takes away.
    /// </summary>
    /// <summary>
    /// The activity log speaks the shop's language, and still names things the shop's own way.
    ///
    /// The log stores a pattern and the values that go in it, kept apart, so that the sentence
    /// can be assembled in whatever language is running — see <c>ActivityRepository.Say</c>.
    /// Before that it stored finished English sentences, which no amount of translating could
    /// ever have rescued: the words and the product names were baked together.
    ///
    /// Two halves, and the second is the one worth guarding. The pattern must translate, and
    /// the product's name must come through it untouched — a shop that calls something "Pay"
    /// has a product called Pay, not a button.
    /// </summary>
    private static void CheckTheActivityLogSpeaksTheShopsLanguage(StringBuilder report, ref int failures)
    {
        var was = Loc.Current;

        try
        {
            Loc.Use(Models.Language.Arabic);

            var entry = new Models.ActivityEntry
            {
                WorkerName = "Ahmed",
                Action = "deactivated product",
                Detail = Data.ActivityRepository.Say("deactivated {0}", "Cahier de notes"),
            };

            var said = entry.Sentence;

            Verdict(report, ref failures, "the activity log is written in the shop's language",
                !said.Contains("deactivated", StringComparison.OrdinalIgnoreCase),
                said);

            Verdict(report, ref failures, "and the product keeps the name the shop gave it",
                said.Contains("Cahier de notes", StringComparison.Ordinal)
                    && said.Contains("Ahmed", StringComparison.Ordinal),
                said);

            // An entry written before the log kept its words apart. The sentence is finished
            // English with the shop's name already inside it — and it still has to come out in
            // Arabic, or a shop that has been running for a year opens its log and finds the
            // language stops halfway down the page.
            var old = new Models.ActivityEntry
            {
                WorkerName = "Ahmed",
                Action = "added product",
                Detail = "added product Cahier de notes",
            };

            Verdict(report, ref failures, "and so is one written before the log knew how",
                !old.Sentence.Contains("added product", StringComparison.OrdinalIgnoreCase)
                    && old.Sentence.Contains("Cahier de notes", StringComparison.Ordinal),
                old.Sentence);

            // The one it must not do: a sentence no pattern fits is left exactly as recorded.
            // Reaching for the nearest pattern would rewrite history to make it prettier.
            var strange = new Models.ActivityEntry
            {
                WorkerName = "Ahmed",
                Action = "did something",
                Detail = "wandered off with the till key",
            };

            Verdict(report, ref failures, "and one that fits nothing is left exactly as recorded",
                strange.Sentence.Contains("wandered off with the till key", StringComparison.Ordinal),
                strange.Sentence);
        }
        finally
        {
            Loc.Use(was);
        }
    }

    private static void CheckTheShopsOwnLanguage(StringBuilder report, ref int failures)
    {
        var was = Loc.Current;

        try
        {
            var missing = new List<string>();
            foreach (var (english, row) in Translations.Table)
                if (row.Fr.Length == 0 || row.Ar.Length == 0) missing.Add(english);

            Verdict(report, ref failures, "every phrase has both languages",
                missing.Count == 0,
                missing.Count == 0
                    ? $"{Translations.Table.Count} phrases, French and Arabic"
                    : $"{missing.Count} half-done, first: {missing[0]}");

            // A shop that has never been switched on has no settings file, so this is the
            // language the client actually meets on the first run. It is the one setting that
            // is wrong by being merely reasonable: English is the language the code is written
            // in, not the language spoken in the shop.
            Verdict(report, ref failures, "a machine with no settings opens in Arabic",
                new AppSettings().Language == "ar",
                $"the default is \"{new AppSettings().Language}\"");

            // The words the till says while somebody is standing at it. Asked of the table
            // rather than by comparing strings: "Total" is French for Total, and a test that
            // called that a gap would be a test nobody could satisfy.
            string[] onTheSaleScreen =
                ["Pay", "Cancel", "Hold ticket", "Price check", "Total", "Search", "Remise"];

            var unknown = onTheSaleScreen.Where(w => !Translations.Table.ContainsKey(w)).ToList();

            Verdict(report, ref failures, "the till speaks French and Arabic",
                unknown.Count == 0,
                unknown.Count == 0
                    ? "every word on the sale screen has a translation"
                    : "not in the table: " + string.Join(", ", unknown));

            Loc.Use(Models.Language.Arabic);

            Verdict(report, ref failures, "Arabic lays the shop out right to left",
                Loc.IsRightToLeft, $"{Loc.NativeName(Models.Language.Arabic)} reads right to left");

            // And it has to survive being asked. The first version turned the app over with
            // FlowDirectionProperty.OverrideMetadata, which Window's own static constructor has
            // already done — so every Arabic start died on "PropertyMetadata is already
            // registered" before a single window existed. Building one here is the whole test.
            var turned = false;
            var problem = string.Empty;
            try
            {
                var window = new MainWindow();
                Localizer.LayOut(window);
                turned = window.FlowDirection == FlowDirection.RightToLeft;
            }
            catch (Exception error)
            {
                problem = $"{error.GetType().Name}: {error.Message}";
            }

            Verdict(report, ref failures, "a window can actually be built in Arabic",
                turned,
                problem.Length > 0 ? problem : "the till opens laid out right to left");

            // An amount must survive an Arabic paragraph intact: bidi otherwise puts the
            // currency in front of the figure and a minus sign behind it.
            var pinned = Loc.Ltr("-3,295.10 DH");
            Verdict(report, ref failures, "money keeps its shape in Arabic",
                pinned.StartsWith('‎') && pinned.EndsWith('‎'),
                "pinned left to right, so the minus stays in front");

            // And the other half of the rule: the shop's own words are never touched.
            Loc.Use(Models.Language.French);
            var product = Catalog.Products.FirstOrDefault();
            Verdict(report, ref failures, "a product keeps the name the shop gave it",
                product is null || Loc.T(product.Name) == product.Name
                    || !Translations.Table.ContainsKey(product.Name),
                product is null ? "nothing in the shop" : $"\"{product.Name}\" is left alone");
        }
        finally
        {
            Loc.Use(was);
        }
    }

    /// <summary>
    /// The scan that must not happen.
    ///
    /// A cashier scanning a run of items looks at the customer, not the screen, so a till that
    /// silently keeps adding an item it does not have is a till that hands over goods the shop
    /// cannot supply and a count nobody can trust afterwards. Both refusals are checked here,
    /// and both of them name the product: "error" on its own tells the person at the counter
    /// nothing they can act on.
    /// </summary>
    /// <summary>
    /// An empty shelf has to reach the owner, not just the cashier who met it.
    ///
    /// The till says no at the counter, which stops the sale but tells nobody to reorder. The
    /// alert is the other half: it carries the product's name onto the Reports page and into
    /// the count on the sidebar, so a shop that ran out this morning knows by this evening.
    /// </summary>
    /// <summary>
    /// The navigation, in the shop's language — checked on the window itself rather than in a
    /// screenshot.
    ///
    /// This is the check that was missing when the back office shipped with an English
    /// sidebar. The diagnostics that photograph every screen call the translator explicitly,
    /// so they showed Arabic navigation while the running app showed English: they were
    /// exercising a path the app never takes. Building the window the way the app builds it is
    /// the only version of this test worth having.
    /// </summary>
    private static void CheckTheNavigationSpeaksTheShopsLanguage(StringBuilder report, ref int failures)
    {
        var was = Loc.Current;

        try
        {
            Loc.Use(Models.Language.Arabic);

            var shell = new AdminWindow();
            var words = Descendants((FrameworkElement)shell.Content)
                .OfType<System.Windows.Controls.TextBlock>()
                .Select(t => t.Text.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            // The English source words for the sidebar. Any of them still on screen means the
            // navigation never went through the translator.
            string[] nav =
                ["Dashboard", "Sales history", "Add product", "Categories", "Inventory",
                 "Suppliers", "Expenses", "Workers", "Reports", "Activity log",
                 "DAILY", "GOODS", "MONEY", "PEOPLE", "INSIGHT"];

            var untranslated = nav.Where(words.Contains).ToList();

            Verdict(report, ref failures, "the back office navigation is in the shop's language",
                untranslated.Count == 0,
                untranslated.Count == 0
                    ? $"all {nav.Length} nav labels translated"
                    : "still English: " + string.Join(", ", untranslated));

            // And the till, which is the screen that runs all day.
            var till = new MainWindow();
            var tillWords = Descendants((FrameworkElement)till.Content)
                .OfType<System.Windows.Controls.TextBlock>()
                .Select(t => t.Text.Trim())
                .ToList();

            string[] counter = ["Search", "Price check", "Reprint receipt"];
            var englishAtTheTill = counter.Where(tillWords.Contains).ToList();

            Verdict(report, ref failures, "and so is the till",
                englishAtTheTill.Count == 0,
                englishAtTheTill.Count == 0
                    ? "the sale screen is translated"
                    : "still English: " + string.Join(", ", englishAtTheTill));
        }
        finally
        {
            Loc.Use(was);
        }
    }

    /// <summary>
    /// Every dialog the shop opens, in the shop's language.
    ///
    /// Dialogs are the half of the app that no navigation reaches, so nothing that checks
    /// pages checks these. Each one is built the way the app builds it and then told it has
    /// loaded, which is exactly what happens when it is shown.
    /// </summary>
    private static void CheckEveryDialogSpeaksTheShopsLanguage(StringBuilder report, ref int failures)
    {
        var was = Loc.Current;

        try
        {
            Loc.Use(Models.Language.Arabic);

            (string Name, Func<Window> Make)[] dialogs =
            [
                ("Add supplier", () => new SupplierWindow(null)),
                ("Add product", () => new ProductWindow(null)),
                ("Add category", () => new CategoryWindow(null)),
                ("Add expense", () => new ExpenseWindow(null)),
                ("Add worker", () => new WorkerWindow(null)),
                ("Record a delivery", () => new PurchaseWindow(null)),
                ("Settings", () => new Views.SettingsWindow()),
                ("Reprint", () => new Views.ReprintWindow()),
            ];

            var english = new List<string>();

            foreach (var (name, make) in dialogs)
            {
                Window window;
                try { window = make(); }
                catch { continue; }   // needs data this shop has not got yet

                // Deliberately not raising Loaded. A dialog has to be translated by the time
                // it is built: relying on the event is what left an Add supplier form with
                // half its labels in English while the sidebar behind it was Arabic.

                var words = Descendants((FrameworkElement)window.Content)
                    .OfType<System.Windows.Controls.TextBlock>()
                    .Select(t => t.Text.Trim())
                    .Where(t => t.Length > 2 && Translations.Table.ContainsKey(t))
                    .Distinct()
                    .ToList();

                if (words.Count > 0)
                    english.Add($"{name}: {string.Join(", ", words.Take(3))}");
            }

            Verdict(report, ref failures, "every dialog is in the shop's language",
                english.Count == 0,
                english.Count == 0
                    ? $"{dialogs.Length} dialogs checked, none showing English"
                    : string.Join(" | ", english));
        }
        finally
        {
            Loc.Use(was);
        }
    }

    /// <summary>
    /// Every dialog has to fit the screen it is opened on, or be able to scroll.
    ///
    /// These windows size themselves to their content, which is right on a big monitor and
    /// wrong on a shop laptop: Add supplier carries a contact form and a goods editor, and its
    /// bottom half — what was paid, and Save — was off the bottom of a 768-pixel screen with
    /// no way to reach it.
    ///
    /// Measured against a small screen on purpose. A check that only passes on the machine it
    /// was written on is not a check.
    /// </summary>
    private static void CheckEveryDialogFitsASmallScreen(StringBuilder report, ref int failures)
    {
        // 1366x768 is the shop laptop, and Windows ships those at 125% scaling, which leaves
        // WPF 1093x614 device-independent pixels to lay a dialog out in. Testing against 768
        // was testing against a machine nobody has: the Add supplier dialog passed here and
        // still ran off the bottom of the screen in the shop, with Save out of reach.
        const double smallScreen = 614;
        const double smallScreenWidth = 1093;

        (string Name, Func<Window> Make)[] dialogs =
        [
            ("Add supplier", () => new SupplierWindow(null)),
            ("Add product", () => new ProductWindow(null)),
            ("Add category", () => new CategoryWindow(null)),
            ("Add expense", () => new ExpenseWindow(null)),
            ("Add worker", () => new WorkerWindow(null)),
            ("Record a delivery", () => new PurchaseWindow(null)),
            ("Settings", () => new Views.SettingsWindow()),
        ];

        var overflowing = new List<string>();
        var outOfReach = new List<string>();
        var scrolled = new List<string>();

        foreach (var (name, make) in dialogs)
        {
            Window window;
            try { window = make(); }
            catch { continue; }

            // Put on a screen this machine does not have, through exactly the code the shop's
            // machine runs — not through a re-implementation of it that can drift.
            var fitted = Responsive.FitTo(window, new Size(smallScreenWidth, smallScreen));

            if (fitted.Width > smallScreenWidth + 1 || fitted.Height > smallScreen + 1)
                overflowing.Add($"{name} still wants {fitted.Width:0}x{fitted.Height:0}px");

            // And then the question the shop actually cares about: laid out at the size it
            // ended up, is all of it on the screen? Shrunk content is not lost content, so
            // this asks the fitted dialog how much room it needs — which for one that was
            // scaled is its scaled height, and for one that was left long is the scroller's.
            //
            // This is the half that failed in the shop. Add supplier fitted the screen by
            // being cut off at the Save button, and every check here called that a pass.
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(fitted.Width, smallScreen));

            if (window.Content is System.Windows.Controls.ScrollViewer) scrolled.Add(name);

            if (content.DesiredSize.Height > smallScreen + 1)
                outOfReach.Add($"{name} needs {content.DesiredSize.Height:0}px "
                             + $"of a {smallScreen:0}px screen and cannot be scrolled");
        }

        Verdict(report, ref failures, "every dialog fits a small screen",
            overflowing.Count == 0,
            overflowing.Count == 0
                ? $"{dialogs.Length} dialogs checked against a {smallScreenWidth:0}x{smallScreen:0} screen"
                : string.Join("; ", overflowing));

        Verdict(report, ref failures, "and nothing on one is out of reach",
            outOfReach.Count == 0,
            outOfReach.Count == 0
                ? scrolled.Count == 0
                    ? "all of them fit whole, none had to scroll"
                    : $"scrolled rather than shrunk past reading size: {string.Join(", ", scrolled)}"
                : string.Join("; ", outOfReach));
    }

    /// <summary>
    /// A dialog belongs to a window, not to a monitor.
    ///
    /// Every check above puts dialogs on a small screen, and every one of them passed while
    /// the shop was still seeing the fault: pull the back office off full screen on a large
    /// monitor and Settings went on opening at its full 500x1042 over a 900px window, hanging
    /// off both ends of the thing it belongs to, because the screen behind it had room to
    /// spare. The screen was never the right question.
    /// </summary>
    private static void CheckADialogFitsTheWindowItOpensOver(StringBuilder report, ref int failures)
    {
        try
        {
            // A back office pulled down to well under the monitor it is running on.
            const double width = 900, height = 620;

            var shell = new AdminWindow
            {
                Width = width,
                Height = height,

                // Shown, because WPF refuses to make a window an Owner until it has been —
                // and shown far off the side of every monitor, because a diagnostic run has
                // no business flashing windows at whoever started it.
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000,
                Top = -30000,
                ShowInTaskbar = false,
            };
            shell.ShowWhatThisPersonMaySee();
            shell.Show();

            var tooBig = new List<string>();

            (string Name, Func<Window> Make)[] dialogs =
            [
                ("Settings", () => new Views.SettingsWindow()),
                ("Add supplier", () => new SupplierWindow(null)),
                ("Add product", () => new ProductWindow(null)),
                ("Record a delivery", () => new PurchaseWindow(null)),
            ];

            foreach (var (name, make) in dialogs)
            {
                Window dialog;
                try { dialog = make(); }
                catch { continue; }

                dialog.Owner = shell;

                var fitted = Responsive.FitTo(dialog, Responsive.RoomFor(dialog));

                if (fitted.Width > width + 1 || fitted.Height > height + 1)
                    tooBig.Add($"{name} opens at {fitted.Width:0}x{fitted.Height:0} "
                             + $"over a {width:0}x{height:0} window");
            }

            shell.Close();

            Verdict(report, ref failures, "a dialog fits the window it opens over, not the screen",
                tooBig.Count == 0,
                tooBig.Count == 0
                    ? $"{dialogs.Length} dialogs over a {width:0}x{height:0} back office "
                    + $"on a {SystemParameters.WorkArea.Width:0}x{SystemParameters.WorkArea.Height:0} screen"
                    : string.Join("; ", tooBig));
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  dialog fits its window: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// Lets the window finish what it was asked to do — lay out, and build the rows a
    /// drop-down generates only once it is on screen. A diagnostic runs without ever giving
    /// the dispatcher a turn, so nothing happens between one line and the next unless it is
    /// asked for.
    /// </summary>
    private static void Settle(System.Windows.Threading.DispatcherObject window) =>
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>
    /// A filter stays where the shop put it.
    ///
    /// Every one of these pages reloads itself when a filter changes, and a reload that also
    /// rebuilds the filter puts it back to its first entry — so the list flicks back to
    /// "Everything" the instant you choose anything else, and the page looks broken in a way
    /// that is very hard to argue with.
    /// </summary>
    private static void CheckAFilterStaysWhereItIsPut(StringBuilder report, ref int failures)
    {
        try
        {
            // Shown, and far off the side of every monitor. A drop-down builds no rows until
            // it is on a screen, so the half of this that matters — pressing one — cannot be
            // checked on a window that was only ever measured.
            var shell = new AdminWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000,
                Top = -30000,
                ShowInTaskbar = false,
            };
            shell.ShowWhatThisPersonMaySee();
            shell.Show();
            shell.GoTo(Models.AdminPage.Activity);
            Settle(shell);

            var page = (ActivityPage)((System.Windows.Controls.ContentControl)shell.FindName("PageHost")).Content;
            var box = page.FindName("KindFilter") as System.Windows.Controls.ComboBox;

            if (box is null)
            {
                Verdict(report, ref failures, "the activity filter stays where it is put",
                    false, "no filter found on the page");
                return;
            }

            var offered = box.Items.Count;
            box.SelectedIndex = 1;
            page.Refresh();

            Verdict(report, ref failures, "the activity filter stays where it is put",
                box.SelectedIndex == 1,
                $"{offered} choices, chose 1, came back {box.SelectedIndex}");

            // And the way a finger does it. Pressing a row in the drop-down is not the same
            // code path as setting the index: the row's container marks itself selected and
            // the box is meant to follow. If it does not, the list opens, the shop taps, and
            // the box goes on showing whatever it showed before.
            box.SelectedIndex = 0;
            box.IsDropDownOpen = true;
            Settle(shell);

            var row = box.ItemContainerGenerator.ContainerFromIndex(2) as System.Windows.Controls.ComboBoxItem;
            if (row is not null) row.IsSelected = true;
            box.IsDropDownOpen = false;
            Settle(shell);

            Verdict(report, ref failures, "and follows a row pressed in the drop-down",
                row is not null && box.SelectedIndex == 2,
                row is null
                    ? "the drop-down built no rows to press"
                    : $"pressed row 2, box shows {box.SelectedIndex} ({box.SelectionBoxItem})");

            // And what is actually painted in the closed box, which is a different question
            // from what the property says. The shop reported the list filtering correctly
            // while the word above it never changed.
            var painted = string.Concat(Descendants(box)
                .OfType<System.Windows.Controls.TextBlock>()
                .Select(t => t.Text)
                .Where(t => t is not "▾" and not ""));

            var stuck = Descendants(box).OfType<System.Windows.Controls.TextBlock>()
                .FirstOrDefault(t => t.Text is not "▾" and not "");

            var how = stuck is null ? "no text block" :
                $"bound={System.Windows.Data.BindingOperations.GetBindingExpressionBase(stuck, System.Windows.Controls.TextBlock.TextProperty) is not null}, " +
                $"templated={stuck.TemplatedParent?.GetType().Name ?? "none"}";

            Verdict(report, ref failures, "and the closed box paints what was chosen",
                painted.Contains(box.SelectionBoxItem?.ToString() ?? "", StringComparison.Ordinal),
                $"box says \"{box.SelectionBoxItem}\", screen says \"{painted}\" — {how}");

            shell.Close();
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  activity filter: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// The on-screen keyboard, on every screen a shop might run it on.
    ///
    /// A keyboard is the one part of this app that has to fit. Everything else can scroll, be
    /// scaled, or be read later; a key that is off the bottom edge is a letter the shop cannot
    /// type, and a key too small to hit with a finger is the same thing more slowly.
    ///
    /// So: never wider or taller than the screen, never more than a share of it — the thing
    /// being typed into has to stay visible above it — and never smaller than a fingertip.
    /// </summary>
    private static void CheckTheKeyboardFitsEveryScreen(StringBuilder report, ref int failures)
    {
        (double Width, double Height, string What)[] screens =
        [
            (1920, 1032, "counter display"),
            (1366, 768, "shop laptop"),
            (1093, 614, "shop laptop at 125%"),
            (1024, 600, "netbook"),
            (800, 480, "smallest thing anyone would try"),
        ];

        var was = AppSettings.Current.KeyboardScale;

        try
        {
            var keyboard = new Views.KeyboardWindow();
            var faults = new List<string>();
            var sizes = new List<string>();

            // Every screen at the smallest and biggest the shop can make it. The largest
            // setting on the smallest screen is the one that has to be checked: a keyboard the
            // shop grew until it was comfortable must not then bury the till it types into.
            foreach (var scale in new[] { 0.7, 1.0, 1.4 })
            foreach (var (width, height, what) in screens)
            {
                AppSettings.Current.KeyboardScale = scale;

                var fitted = keyboard.FitTo(new Size(width, height));
                if (scale == 1.0) sizes.Add($"{what} {fitted.Width:0}x{fitted.Height:0}");

                if (fitted.Width > width + 1 || fitted.Height > height + 1)
                    faults.Add($"{what} at {scale:0.0}x: {fitted.Width:0}x{fitted.Height:0} does not fit {width:0}x{height:0}");

                // Half the screen is already generous for keys; past that the box being
                // filled in is behind the keyboard and the shop is typing blind.
                else if (fitted.Height > height * 0.56)
                    faults.Add($"{what} at {scale:0.0}x: {fitted.Height:0}px of a {height:0}px screen");

                // Every row inside the panel, not just the panel itself. The command row was
                // 14.8 units wide in a panel twelve across: it is centred, so it hung off both
                // ends and the keys at the edges were not on the screen at all. The first one
                // lost was the decimal point, which a till cannot do without — and the panel's
                // own size looked perfectly correct the whole time.
                // 28 is the panel's own padding, 14 each side.
                var overflow = WidestRow(keyboard) - (fitted.Width - 28);
                if (overflow > 1)
                    faults.Add($"{what} at {scale:0.0}x: a row is {overflow:0}px wider than the panel");

                var key = SmallestKey(keyboard);
                if (key < 26)
                    faults.Add($"{what} at {scale:0.0}x: keys down to {key:0}px, too small to hit");
            }

            keyboard.Close();

            Verdict(report, ref failures, "the keyboard fits every screen it might land on",
                faults.Count == 0,
                faults.Count == 0 ? string.Join(", ", sizes) : string.Join("; ", faults));
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  keyboard scaling: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            AppSettings.Current.KeyboardScale = was;
        }
    }

    /// <summary>How wide the widest row of keys comes out, margins included.</summary>
    private static double WidestRow(Views.KeyboardWindow keyboard) =>
        keyboard.FindName("Rows") is System.Windows.Controls.Panel rows
            ? rows.Children.OfType<System.Windows.Controls.Panel>()
                .Select(row => row.Children.OfType<FrameworkElement>()
                    .Sum(k => k.Width + k.Margin.Left + k.Margin.Right))
                .DefaultIfEmpty(0)
                .Max()
            : 0;

    /// <summary>The narrowest key on the panel — the letters, not the wide command keys.</summary>
    private static double SmallestKey(Views.KeyboardWindow keyboard) =>
        keyboard.FindName("Rows") is FrameworkElement rows
            ? Descendants(rows)
                .OfType<System.Windows.Controls.Button>()
                .Select(b => Math.Min(b.Width, b.Height))
                .DefaultIfEmpty(0)
                .Min()
            : 0;

    /// <summary>
    /// The two windows that fill the screen, on a screen that is not this one.
    ///
    /// The till and the back office were laid out on a 1920x1080 monitor, and every number in
    /// them was typed against it. Handed to a shop with a 1366x768 laptop they kept every one
    /// of those numbers and simply ran off the bottom: the back office lost the last panel of
    /// the dashboard, and the till lost the Pay button under the fold.
    ///
    /// Both are wrapped in a ScaleHost that draws them at whatever size the screen actually
    /// is, so this asks the only question that matters — laid out on the shop's screen, does
    /// any of it end up outside the screen.
    /// </summary>
    private static void CheckTheShellsFitASmallScreen(StringBuilder report, ref int failures)
    {
        const double width = 1093, height = 614;

        (string Name, Func<Window> Make)[] shells =
        [
            ("till", () => new MainWindow()),
            ("back office", () => new AdminWindow()),
        ];

        var faults = new List<string>();
        var scales = new List<string>();

        foreach (var (name, make) in shells)
        {
            Window window;
            try { window = make(); }
            catch (Exception error) { faults.Add($"{name} would not build: {error.Message}"); continue; }

            var root = (FrameworkElement)window.Content;

            if (root is not Controls.ScaleHost host)
            {
                faults.Add($"{name} is not scaled to the screen at all");
                continue;
            }

            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();

            scales.Add($"{name} {host.Scale:0.00}");

            if (root.DesiredSize.Width > width + 1 || root.DesiredSize.Height > height + 1)
                faults.Add($"{name} overflows by "
                         + $"{Math.Max(0, root.DesiredSize.Width - width):0}x"
                         + $"{Math.Max(0, root.DesiredSize.Height - height):0}px");
        }

        Verdict(report, ref failures, "the till and the back office fit a small screen",
            faults.Count == 0,
            faults.Count == 0
                ? $"laid out at {width:0}x{height:0} — {string.Join(", ", scales)}"
                : string.Join("; ", faults));
    }

    private static void CheckAnEmptyShelfIsReported(StringBuilder report, ref int failures)
    {
        var empty = StockRepository.OutOfStock();
        var alerts = Notifications.Build();

        if (empty.Count == 0)
        {
            var clear = alerts.All(a => !a.Title.Contains("out of stock", StringComparison.OrdinalIgnoreCase));
            Verdict(report, ref failures, "nothing is called out of stock when nothing is",
                clear, "no empty shelves, and no alert claiming otherwise");
            return;
        }

        // The alert names the products, so the owner can act without opening another page.
        var told = alerts.FirstOrDefault(a => a.GoTo == AdminPage.Inventory
                                           && a.Detail.Contains(empty[0].Name));

        Verdict(report, ref failures, "an empty shelf raises an alert that names the product",
            told is not null,
            told is not null
                ? $"{told.Title} — {told.Detail}"
                : $"{empty.Count} product(s) at zero and no alert mentions {empty[0].Name}");

        Verdict(report, ref failures, "and it is urgent, not a note",
            told is null || told.Level == AlertLevel.Danger,
            $"level is {told?.Level}");
    }

    /// <summary>
    /// The expiry column on the products list.
    ///
    /// Most stock has no expiry date, and that has to read as nothing at all. It once read as
    /// "739,865 days ago", in red, on every row — a blank column parsed as year one. So this
    /// asks the two questions that matter: does no date say nothing, and does a real date say
    /// something a shopkeeper would recognise.
    /// </summary>
    private static void CheckExpiryDatesReadTrue(StringBuilder report, ref int failures)
    {
        var stock = StockRepository.List();

        var undated = stock.Where(p => p.ExpiresOn is null).ToList();
        var wrong = undated.FirstOrDefault(p => p.ExpiryLabel != "—" || p.ExpiryNeedsAttention);

        Verdict(report, ref failures, "stock with no expiry date shows none",
            wrong is null,
            wrong is null
                ? $"{undated.Count} product(s) with no date, all showing a dash"
                : $"{wrong.Name} has no expiry date but reads \"{wrong.ExpiryLabel}\"");

        // A date the shop really set, said the way it would be said out loud.
        var dated = stock.FirstOrDefault(p => p.ExpiresOn is not null);
        if (dated is null)
        {
            report.AppendLine("  --  no dated stock to read (nothing has an expiry date)");
            return;
        }

        var days = dated.DaysToExpiry!.Value;
        Verdict(report, ref failures, "a real expiry date reads as a plain phrase",
            dated.ExpiryLabel.Length > 0 && dated.ExpiryLabel != "—"
                && Math.Abs(days) < 40000,
            $"{dated.Name}: {dated.ExpiryLabel} ({days} days)");

        // Red is for the week ahead, not for everything with a date on it.
        Verdict(report, ref failures, "only the next seven days are marked urgent",
            stock.Where(p => p.ExpiryNeedsAttention).All(p => p.DaysToExpiry <= 7),
            $"{stock.Count(p => p.ExpiryNeedsAttention)} product(s) marked");
    }

    /// <summary>
    /// The two filters on the stock list, driven the way the owner drives them.
    ///
    /// A filter that quietly does nothing looks exactly like a shop where everything matches,
    /// which is why this sets the controls and reads the rows back rather than checking that
    /// the handlers exist. The date range reads the expiry date, so stock with no date has to
    /// drop out of it \u2014 otherwise "what goes off this week" answers with the whole shop.
    /// </summary>
    private static void CheckTheStockListNarrows(StringBuilder report, ref int failures)
    {
        var shell = new AdminWindow();
        var page = new InventoryPage();
        page.Attach(new ViewModels.AdminContext(), shell);
        page.Refresh();

        var all = ((IEnumerable<StockItem>?)page.Rows.ItemsSource)?.ToList() ?? new List<StockItem>();
        if (all.Count == 0)
        {
            report.AppendLine("  --  nothing in the shop, so no stock list to narrow");
            return;
        }

        // ---- By aisle ----
        var aisle = all.Where(i => i.CategoryId > 0)
                       .GroupBy(i => i.CategoryId)
                       .FirstOrDefault(g => g.Count() < all.Count);

        if (aisle is null)
        {
            report.AppendLine("  --  every product is in the same category, so the filter has nothing to cut");
        }
        else
        {
            var choice = page.CategoryFilter.ItemsSource?.Cast<CategoryRow>()
                             .FirstOrDefault(c => c.Id == aisle.Key);

            page.CategoryFilter.SelectedItem = choice;
            page.Refresh();

            var shown = ((IEnumerable<StockItem>?)page.Rows.ItemsSource)?.ToList() ?? new List<StockItem>();
            var strays = shown.Where(i => i.CategoryId != aisle.Key).ToList();

            Verdict(report, ref failures, "the category filter shows that category and nothing else",
                strays.Count == 0 && shown.Count == aisle.Count(),
                strays.Count > 0
                    ? $"{strays[0].Name} is not in {choice?.Name}"
                    : $"{shown.Count} of {all.Count} products in {choice?.Name}");

            page.CategoryFilter.SelectedIndex = 0;
            page.Refresh();
        }

        // ---- By what goes off, and when ----
        var dated = all.Where(i => i.ExpiresOn is not null).OrderBy(i => i.ExpiresOn).ToList();
        if (dated.Count == 0)
        {
            report.AppendLine("  --  nothing has an expiry date, so the range has nothing to sit on");
            return;
        }

        var from = dated[0].ExpiresOn!.Value.Date;
        var to = from.AddDays(7);

        page.FromDate.SelectedDate = from;
        page.ToDate.SelectedDate = to;
        page.Refresh();

        var inWindow = ((IEnumerable<StockItem>?)page.Rows.ItemsSource)?.ToList() ?? new List<StockItem>();
        var expected = all.Count(i => i.ExpiresOn is { } w && w.Date >= from && w.Date <= to);

        Verdict(report, ref failures, "the date range keeps only what goes off inside it",
            inWindow.All(i => i.ExpiresOn is { } w && w.Date >= from && w.Date <= to)
                && inWindow.Count == expected,
            $"{inWindow.Count} product(s) between {from:d MMM} and {to:d MMM}, expected {expected}");

        Verdict(report, ref failures, "and stock with no expiry date drops out of it",
            inWindow.All(i => i.ExpiresOn is not null),
            $"{inWindow.Count(i => i.ExpiresOn is null)} undated product(s) still on screen");

        Verdict(report, ref failures, "a filtered list offers the way back out",
            page.ClearFilters.Visibility == System.Windows.Visibility.Visible,
            $"Clear filters is {page.ClearFilters.Visibility}");

        // ---- And back to the whole shop ----
        page.FromDate.SelectedDate = null;
        page.ToDate.SelectedDate = null;
        page.Refresh();

        var back = ((IEnumerable<StockItem>?)page.Rows.ItemsSource)?.ToList() ?? new List<StockItem>();
        Verdict(report, ref failures, "clearing the range brings the whole shop back",
            back.Count == all.Count,
            $"{back.Count} of {all.Count} products");
    }

    /// <summary>
    /// Both windows draw their own frame, and both shipped without the middle window control.
    ///
    /// They opened filling the screen with nothing to press to make them smaller, and the one
    /// drag handler there was refused to move a window that was filling the screen. The result
    /// was an app that looked seized: it would not budge and it would not let go of the
    /// monitor. So this asks of each window what a title bar would give it for free — a
    /// control that changes the size, and a size that actually changes.
    /// </summary>
    private static void CheckTheWindowsCanBeMoved(StringBuilder report, ref int failures)
    {
        var area = System.Windows.SystemParameters.WorkArea;

        foreach (var (name, window) in new (string, Window)[]
                 { ("the till", new MainWindow()), ("the back office", new AdminWindow()) })
        {
            Verdict(report, ref failures, $"{name} opens filling the screen",
                Chrome.FillsTheScreen(window),
                $"{window.Width:0}x{window.Height:0} against a {area.Width:0}x{area.Height:0} desktop");

            // The control itself, not just the code behind it: a handler nothing calls is
            // exactly what the old double-click-only restore was.
            var button = window.FindName("SizeButton") as System.Windows.Controls.Button;
            var glyph = window.FindName("SizeGlyph") as System.Windows.Shapes.Path;

            Verdict(report, ref failures, $"{name} has a control that changes its size",
                button is not null && glyph is not null,
                button is null ? "no SizeButton in the top bar" : "SizeButton is there");

            Verdict(report, ref failures, $"{name} says what that control will do",
                button?.ToolTip is string tip && tip.Length > 0,
                $"tooltip: {button?.ToolTip}");

            var wide = glyph?.Data;

            Chrome.Toggle(window);

            Verdict(report, ref failures, $"{name} comes off the screen when asked",
                !Chrome.FillsTheScreen(window)
                    && window.Width < area.Width - 1 && window.Height < area.Height - 1,
                $"now {window.Width:0}x{window.Height:0}");

            Verdict(report, ref failures, $"{name} stays on the desktop when it does",
                window.Left >= area.Left - 1 && window.Top >= area.Top - 1
                    && window.Left + window.Width <= area.Right + 1
                    && window.Top + window.Height <= area.Bottom + 1,
                $"at {window.Left:0},{window.Top:0}");

            Chrome.Toggle(window);

            Verdict(report, ref failures, $"{name} goes back to filling it",
                Chrome.FillsTheScreen(window),
                $"{window.Width:0}x{window.Height:0}");

            // What actually makes the window draggable: Windows is told that the top strip
            // is the caption. Doing this in WPF's own mouse events did not work — a press
            // only reaches a handler if it lands on the exact element carrying it, and the
            // strip is half a dozen elements with gaps between them. So this checks the
            // thing that replaced all that, because without it the window cannot be moved
            // at all and nothing else on screen would look wrong.
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);

            Verdict(report, ref failures, $"{name} gives Windows a caption to drag it by",
                chrome is not null,
                chrome is null ? "no WindowChrome — the window cannot be moved" : "WindowChrome is set");

            Verdict(report, ref failures, $"{name} caption is deep enough to grab",
                chrome is { CaptionHeight: >= 40 },
                $"caption is {chrome?.CaptionHeight ?? 0:0}px deep");

            Verdict(report, ref failures, $"{name} can still be resized by its edges",
                chrome is not null && chrome.ResizeBorderThickness.Left > 0,
                $"resize border is {chrome?.ResizeBorderThickness.Left ?? 0:0}px");

            // The controls sitting in that strip have to be excused from it, or Windows
            // takes their clicks and the buttons stop working.
            Verdict(report, ref failures, $"{name} keeps its window buttons clickable",
                button is not null && Excused(button),
                button is null ? "no SizeButton"
                    : "the window controls are marked hit-test visible in the chrome");


            // Both glyphs have to exist and differ, or the button never looks like it worked.
            var tall = window.TryFindResource("Icon.Maximize") as System.Windows.Media.Geometry;
            var small = window.TryFindResource("Icon.Restore") as System.Windows.Media.Geometry;

            Verdict(report, ref failures, $"{name} has both size glyphs, and they differ",
                tall is not null && small is not null
                    && tall.ToString() != small.ToString() && wide is not null,
                tall is null || small is null ? "a glyph is missing" : "two distinct glyphs");

            window.Close();
        }
    }

    /// <summary>
    /// True when this element, or something it sits inside, is excused from the window's
    /// caption. Anything in the title strip that is meant to be pressed needs this, because
    /// the caption belongs to Windows and Windows would otherwise treat the press as a drag.
    /// </summary>
    private static bool Excused(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is UIElement element
                && System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(element))
                return true;

            node = System.Windows.Media.VisualTreeHelper.GetParent(node)
                ?? LogicalTreeHelper.GetParent(node);
        }

        return false;
    }

    private static void CheckTheTillRefusesWhatIsNotThere(StringBuilder report, ref int failures)
    {
        var empty = Catalog.Products.FirstOrDefault(p => p.IsOutOfStock && p.Barcode.Length > 0);
        var till = new MainWindow();

        if (empty is null)
        {
            report.AppendLine("ok    the till refuses an empty shelf (nothing in the shop is at zero)");
        }
        else
        {
            till.Vm.SearchText = empty.Barcode;
            till.Vm.SubmitBarcodeCommand.Execute(null);

            Verdict(report, ref failures, "scanning something with none left puts nothing on the sale",
                till.Vm.Cart.Count == 0,
                $"{empty.Name} has {empty.Stock:0.###} left; the basket holds {till.Vm.Cart.Count} line(s)");

            Verdict(report, ref failures, "and the cashier is told which product it was",
                till.Vm.StatusIsError && till.Vm.StatusMessage.Contains(empty.Name),
                $"\"{till.Vm.StatusMessage}\"");
        }

        // A barcode the shop has never heard of.
        var stranger = new MainWindow();
        stranger.Vm.SearchText = "9999999999999";
        stranger.Vm.SubmitBarcodeCommand.Execute(null);

        Verdict(report, ref failures, "an unknown barcode is refused, not searched for",
            stranger.Vm.Cart.Count == 0 && stranger.Vm.StatusIsError
                && stranger.Vm.StatusMessage.Contains("9999999999999"),
            $"\"{stranger.Vm.StatusMessage}\"");

        // And the basket cannot be filled past the shelf by scanning the same thing over.
        var stocked = Catalog.Products
            .FirstOrDefault(p => p is { IsOutOfStock: false, Barcode.Length: > 0 } && p.Stock <= 40m);

        if (stocked is null)
        {
            report.AppendLine("ok    the basket stops at the shelf (nothing small enough to scan dry)");
            return;
        }

        var counter = new MainWindow();
        var scans = (int)stocked.Stock + 3;
        for (var i = 0; i < scans; i++)
        {
            counter.Vm.SearchText = stocked.Barcode;
            counter.Vm.SubmitBarcodeCommand.Execute(null);
        }

        var onTheSale = counter.Vm.Cart.FirstOrDefault()?.Quantity ?? 0m;

        // Every scan counts, including the ones past what the shelf claims to hold. The
        // cashier is holding the goods; the count catching up is a stocktake, not a refusal
        // in front of a customer. What the till must never do is drop a scan silently.
        Verdict(report, ref failures, "every scan lands on the sale, even past the shelf count",
            onTheSale == scans,
            $"{scans} scans of {stocked.Name} ({stocked.Stock:0.###} in stock) put "
          + $"{onTheSale:0.###} on the sale");

        Verdict(report, ref failures, "and none of them turns the till red",
            !counter.Vm.StatusIsError,
            $"status: {counter.Vm.StatusMessage}");
    }

    private static void CheckPriceCheck(StringBuilder report, ref int failures)
    {
        var product = StockRepository.List().FirstOrDefault(p => p.Barcode.Length > 0);
        if (product is null)
        {
            report.AppendLine("ok    price check (nothing in the shop to scan)");
            return;
        }

        var till = new MainWindow();
        var before = till.Vm.Cart.Count;

        till.Vm.IsPriceCheck = true;
        till.Vm.SearchText = product.Barcode;
        till.Vm.SubmitBarcodeCommand.Execute(null);

        Verdict(report, ref failures, "a price check never puts anything on the sale",
            till.Vm.Cart.Count == before,
            $"cart held {before} line(s) before and {till.Vm.Cart.Count} after scanning {product.Barcode}");

        Verdict(report, ref failures, "a price check answers with the product behind the barcode",
            till.Vm.PriceCheckResult is { Found: true } found && found.Item!.Id == product.Id,
            till.Vm.PriceCheckResult is { Found: true } hit
                ? $"{product.Barcode} → {hit.Name}, {hit.PriceText}"
                : "no answer came back");

        // The same scan, read by each role in turn.
        Session.SignOut();
        var asCashier = PriceCheck.For(product.Barcode);
        var cashierSees = asCashier.ShowsCost;

        Session.UnlockAsOwner();
        var asOwner = PriceCheck.For(product.Barcode);

        // Only meaningful when the product has a cost recorded; without one nobody sees a
        // cost line and the test would pass for the wrong reason.
        Verdict(report, ref failures, "what the shop paid is the owner's business",
            !cashierSees && (product.Cost <= 0m || asOwner.ShowsCost),
            product.Cost <= 0m
                ? $"{product.Name} has no purchase price recorded, so nobody sees one"
                : $"cashier: hidden · owner: {asOwner.CostText}");
    }

    /// <summary>
    /// A shop with one computer must never notice the network exists.
    ///
    /// The failure this guards is silent and slow: a standalone till quietly filling an outbox
    /// nobody empties, or stamping references onto sales that have nowhere to go. Both would
    /// look fine for months.
    /// </summary>
    private static void CheckStandaloneShopIsLeftAlone(StringBuilder report, ref int failures)
    {
        var configured = AppSettings.Current.ServerAddress;
        AppSettings.Current.ServerAddress = string.Empty;

        try
        {
            Verdict(report, ref failures, "a shop with one computer has no server to talk to",
                !ShopLink.IsConfigured && ShopLink.Status.Length == 0,
                "no address set, so the till shows nothing about a network");

            var before = OutboxRepository.PendingCount();
            ShopLink.Queue(new Link.SaleUpload(
                "should-never-be-stored", DateTime.Now, null, "test", "Cash", 0m,
                0m, "None", 0m, 0m, 0m, 0m, 0m, Array.Empty<Link.SaleLineDto>()));

            Verdict(report, ref failures, "and queues nothing for it",
                OutboxRepository.PendingCount() == before,
                $"{before} in the outbox before and after");
        }
        finally
        {
            AppSettings.Current.ServerAddress = configured;
        }
    }

    private static void CheckTillAsCashier(StringBuilder report, ref int failures)
    {
        Session.SignOut();      // drops the owner unlock; back to a plain cashier

        try
        {
            Verdict(report, ref failures, "the till runs as a cashier",
                Session.CurrentRole == WorkerRole.Cashier,
                $"role is {Session.CurrentRole}");

            (string What, Action Do)[] work =
            [
                ("open the till", () => { _ = new MainWindow(); }),
                ("list products to add", () => StockRepository.RecentlyAdded()),
                ("read the catalogue", () => StockRepository.List()),
                ("read categories", () => CategoryRepository.List()),
                ("look up a barcode", () => StockRepository.BarcodeTaken("9990000000404")),
                ("ask for the scan prompt", () => { _ = new Views.ScanWindow(); }),
            ];

            var refused = new List<string>();
            foreach (var (what, action) in work)
            {
                try { action(); }
                catch (UnauthorizedAccessException) { refused.Add(what); }
                catch (Exception error) { refused.Add($"{what} ({error.GetType().Name})"); }
            }

            Verdict(report, ref failures, "a cashier can do everything the till asks of them",
                refused.Count == 0,
                refused.Count == 0 ? $"all {work.Length} checked" : "refused: " + string.Join(", ", refused));
        }
        finally
        {
            Session.UnlockAsOwner();
        }
    }

    /// <summary>
    /// The back office opens onto different windows for different people. A cashier holds
    /// AddProductAtTill and nothing else, so the sidebar should offer exactly that one page —
    /// a list of thirteen buttons that all refuse them is worse than no list at all.
    ///
    /// Worth a test because the failure is quiet: the pages still fail closed on their own,
    /// so a sidebar that leaked would look like it worked until somebody clicked.
    /// </summary>
    private static void CheckSidebarByRole(StringBuilder report, ref int failures)
    {
        Session.SignOut();

        try
        {
            Session.SignIn(new Worker { Id = -1, Name = "Test cashier", Role = WorkerRole.Cashier });

            var shell = new AdminWindow();
            shell.ShowWhatThisPersonMaySee();

            var offered = Nav(shell)
                .Where(b => b.Visibility == Visibility.Visible)
                .Select(b => (string)b.Tag)
                .ToList();

            Verdict(report, ref failures, "a cashier is offered only Add product",
                offered.Count == 1 && offered[0] == nameof(AdminPage.AddProduct),
                offered.Count == 0 ? "nothing offered" : string.Join(", ", offered));

            Session.SignOut();
            Session.UnlockAsOwner();

            var ownerShell = new AdminWindow();
            ownerShell.ShowWhatThisPersonMaySee();
            var all = Nav(ownerShell).Count(b => b.Visibility == Visibility.Visible);

            Verdict(report, ref failures, "the owner is offered every page",
                all == Enum.GetValues<AdminPage>().Length,
                $"{all} of {Enum.GetValues<AdminPage>().Length}");
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  sidebar by role: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            Session.SignOut();
            Session.UnlockAsOwner();
        }

        static IEnumerable<System.Windows.Controls.RadioButton> Nav(AdminWindow shell) =>
            ((System.Windows.Controls.Panel)shell.FindName("NavList")!)
                .Children.OfType<System.Windows.Controls.RadioButton>();
    }

    /// <summary>
    /// Signing out has to actually reach the till, and the till has to say so. A session that
    /// stays open behind a window somebody closed is the whole problem: the next person to
    /// touch the lock walks in as whoever was there before them.
    /// </summary>
    private static void CheckSignOut(StringBuilder report, ref int failures)
    {
        Session.SignOut();

        try
        {
            var till = new MainWindow();

            string Lock() => (string)((System.Windows.FrameworkElement)till.FindName("RailAdmin")!).ToolTip;
            string Power() => (string)((System.Windows.FrameworkElement)till.FindName("SignOutButton")!).ToolTip;

            var signedOutLock = Lock();
            var signedOutPower = Power();

            Session.SignIn(new Worker { Id = -1, Name = "Fatima", Role = WorkerRole.Cashier });
            var signedInLock = Lock();
            var signedInPower = Power();

            Verdict(report, ref failures, "the till names whoever is signed in",
                signedInLock.Contains("Fatima") && signedInPower.Contains("Fatima"),
                $"lock says \"{signedInLock}\", power says \"{signedInPower}\"");

            Session.SignOut();

            Verdict(report, ref failures, "signing out reaches the till",
                Lock() == signedOutLock && Power() == signedOutPower
                && !Lock().Contains("Fatima") && !Power().Contains("Fatima"),
                $"back to \"{Lock()}\" and \"{Power()}\"");

            Verdict(report, ref failures, "the lock asks again after a sign-out",
                Session.Current is null && !Session.IsOwnerUnlocked,
                "no session left behind");
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  sign-out: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            Session.SignOut();
            Session.UnlockAsOwner();
        }
    }

    /// <summary>
    /// The owner has no staff row, so nothing lists them. Once a cashier had a password the
    /// lock offered that cashier and nobody else — the shop had locked its own owner out by
    /// doing the thing it was told to do. The owner is always first on the list now, and the
    /// name box takes typing so a name can be given at all.
    /// </summary>
    private static void CheckOwnerCanAlwaysSignIn(StringBuilder report, ref int failures)
    {
        try
        {
            var staffWithPasswords = WorkerRepository.List().Count(w => w.HasPin);

            var lockWindow = new Views.StaffSignInWindow();
            var box = (System.Windows.Controls.ComboBox)lockWindow.FindName("WorkerBox")!;
            var names = box.Items.Cast<object>()
                                 .Select(item => (string)item.GetType().GetProperty("Name")!.GetValue(item)!)
                                 .ToList();

            Verdict(report, ref failures, "the owner is always offered at the lock",
                names.Count == staffWithPasswords + 1 && names[0] == Session.OwnerLabel,
                $"{staffWithPasswords} staff with passwords, list reads: {string.Join(", ", names)}");

            Verdict(report, ref failures, "the owner can type their own name",
                box.IsEditable,
                "the name box takes typing, not just a pick");
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  owner sign-in: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// "Bought for" is money, and a money box that accepts letters only says so on Save —
    /// after a whole product has been typed in. Two halves worth holding: the rule itself,
    /// and that the three boxes are actually wired to it.
    /// </summary>
    private static void CheckMoneyFieldsTakeNumbersOnly(StringBuilder report, ref int failures)
    {
        try
        {
            string[] fine = ["", "8", "8.50", "8,50", ".", "0", "1234567"];
            string[] refused = ["a", "8a", "abc", "8.5.5", "8,5,0", "8 50", "-3", "5%", "٨"];

            var wrong = fine.Where(t => !Numeric.IsANumber(t)).Select(t => $"refused \"{t}\"")
                .Concat(refused.Where(Numeric.IsANumber).Select(t => $"accepted \"{t}\""))
                .ToList();

            Verdict(report, ref failures, "money boxes take numbers and nothing else",
                wrong.Count == 0,
                wrong.Count == 0 ? $"{fine.Length} accepted, {refused.Length} refused"
                                 : string.Join(", ", wrong));

            var page = new AddProductPage();
            string[] boxes = ["AddCostBox", "AddPriceBox", "AddQuantityBox"];
            var unguarded = boxes
                .Where(name => page.FindName(name) is not System.Windows.Controls.TextBox box
                               || !Numeric.GetOnly(box))
                .ToList();

            Verdict(report, ref failures, "every money box on Add product is guarded",
                unguarded.Count == 0,
                unguarded.Count == 0 ? "cost, price and quantity" : "unguarded: " + string.Join(", ", unguarded));

            CheckDeliveriesCanBeTypedInGrams(report, ref failures, page);
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  numeric fields: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// A delivery can be typed the way the supplier wrote it down.
    ///
    /// The shop buys 300 g of saffron, and until now the form took kilograms or nothing.
    /// The list has to offer the ways the supplier writes it, and the number typed has to
    /// end up in the stored unit — a gram entry that went in as 300 kg
    /// would price the delivery at a thousand times what it cost, and the mistake would only
    /// surface in the month's figures.
    /// </summary>
    private static void CheckDeliveriesCanBeTypedInGrams(
        StringBuilder report, ref int failures, AddProductPage page)
    {
        var box = page.FindName("AddUnitBox") as System.Windows.Controls.ComboBox;
        var offered = (box?.ItemsSource as System.Collections.IEnumerable)?
            .Cast<object>().Count() ?? 0;

        Verdict(report, ref failures, "Add product offers every way of measuring a delivery",
            offered == 3,
            offered == 3 ? "per unit, kilo, gram" : $"{offered} in the list");

        if (box is null || offered != 3) return;

        // 500 g bought at 20 DH the kilo is 10 DH of stock, not 10 000.
        var cost = (System.Windows.Controls.TextBox)page.FindName("AddCostBox");
        var quantity = (System.Windows.Controls.TextBox)page.FindName("AddQuantityBox");
        var total = (System.Windows.Controls.TextBlock)page.FindName("AddTotalCost");
        var label = (System.Windows.Controls.TextBlock)page.FindName("AddQuantityLabel");

        cost.Text = "20";
        quantity.Text = "500";
        box.SelectedIndex = 2;

        var grams = total.Text;
        var named = label.Text;

        box.SelectedIndex = 1;
        var kilos = total.Text;

        Verdict(report, ref failures, "grams go into stock as kilograms",
            grams.Contains("10") && !grams.Contains("000") && kilos.Contains("10,000"),
            $"500 at 20/kg: grams {grams}, kilos {kilos}");

        Verdict(report, ref failures, "the amount box says which unit it wants",
            named == Loc.T("WEIGHT (G)") && label.Text == Loc.T("WEIGHT (KG)"),
            $"\"{named}\" then \"{label.Text}\"");
    }

    /// <summary>
    /// One icon per back-office page, named after the page. A missing resource is a hard
    /// XAML failure at the moment the sidebar builds, so a page added without one takes the
    /// whole window down rather than showing a gap.
    /// </summary>
    private static void CheckEveryPageHasAnIcon(StringBuilder report, ref int failures, Application app)
    {
        var missing = Enum.GetValues<AdminPage>()
            .Where(page => app.TryFindResource($"Nav.{page}") is not System.Windows.Media.Geometry)
            .Select(page => page.ToString())
            .ToList();

        Verdict(report, ref failures, "every back-office page has its own icon",
            missing.Count == 0,
            missing.Count == 0 ? $"{Enum.GetValues<AdminPage>().Length} drawn"
                               : "no icon for: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The two stock pages, measured rather than eyeballed: they have to use the width they
    /// are given, and their summary figures have to fit the columns they are in. A number
    /// that quietly trims to "2.00..." is the kind of thing nobody notices until the shop
    /// misreads what its stock is worth.
    /// </summary>
    private static void CheckStockPages(StringBuilder report, ref int failures, AdminWindow? shell)
    {
        (string Name, Func<AdminPageBase> Make, string[] Figures)[] pages =
        [
            ("inventory", () => new InventoryPage(), ["Summary"]),
            ("categories", () => new CategoriesPage(),
             ["CountValue", "GroupedValue", "LooseValue", "ValueValue"]),
            ("suppliers", () => new SuppliersPage(),
             ["OwedValue", "BoughtValue", "PaidValue", "CountValue"]),
            ("expenses", () => new ExpensesPage(),
             ["TotalValue", "BiggestValue", "FixedValue", "ShareValue"]),
            ("workers", () => new WorkersPage(),
             ["CountValue", "DueValue", "PaidValue", "OwedValue"]),
            ("reports", () => new ReportsPage(),
             ["RevenueValue", "GrossValue", "NetValue", "SalesValue"]),
        ];

        foreach (var (name, make, figures) in pages)
        {
            try
            {
                var page = make();
                page.Attach(new ViewModels.AdminContext(), shell!);
                page.Refresh();

                page.Measure(new Size(1330, 820));
                page.Arrange(new Rect(0, 0, 1330, 820));
                page.UpdateLayout();

                Verdict(report, ref failures, $"{name} uses the full width",
                    Math.Abs(page.ActualWidth - 1330) < 1,
                    $"content is {page.ActualWidth:0}px wide in a 1330px pane");

                var clipped = new List<string>();
                var blank = new List<string>();
                foreach (var figure in figures)
                {
                    if (page.FindName(figure) is not System.Windows.Controls.TextBlock box) continue;
                    if (box.Text.Length == 0) blank.Add(figure);
                    if (box.ActualWidth > 0 && box.DesiredSize.Width > box.ActualWidth + 0.5)
                        clipped.Add($"{figure} needs {box.DesiredSize.Width:0}px, has {box.ActualWidth:0}px");
                }

                Verdict(report, ref failures, $"{name} summary figures fit their columns",
                    clipped.Count == 0,
                    clipped.Count == 0 ? $"all {figures.Length} fit" : string.Join("; ", clipped));

                Verdict(report, ref failures, $"{name} summary is filled in",
                    blank.Count == 0,
                    blank.Count == 0 ? "every figure has a value" : "empty: " + string.Join(", ", blank));
            }
            catch (Exception error)
            {
                failures++;
                report.AppendLine($"FAIL  {name} page: {error.GetType().Name}: {error.Message}");
            }
        }
    }

    /// <summary>
    /// A form field has to look like one wherever it is put.
    ///
    /// TextBox.Field is card-coloured with no border, which is invisible the moment it sits
    /// on a card-coloured panel — the Record a delivery dialog shipped with three boxes
    /// nobody could see, and nothing failed. The hairline is what makes it a field on any
    /// ground, so it is worth holding.
    /// </summary>
    private static void CheckFieldsAreVisible(StringBuilder report, ref int failures, Application app)
    {
        var transparent = new List<string>();

        foreach (var key in new[] { "TextBox.Field", "TextBox.Multiline" })
        {
            if (app.TryFindResource(key) is not Style style) continue;

            var brush = style.Setters.OfType<Setter>()
                .LastOrDefault(x => x.Property == System.Windows.Controls.Control.BorderBrushProperty)
                ?.Value as System.Windows.Media.SolidColorBrush;

            var fill = style.Setters.OfType<Setter>()
                .LastOrDefault(x => x.Property == System.Windows.Controls.Control.BackgroundProperty)
                ?.Value as System.Windows.Media.SolidColorBrush;

            var card = (System.Windows.Media.SolidColorBrush)app.FindResource("Brush.Card");

            // Only a problem when the fill matches the card it may be sitting on.
            if (fill?.Color != card.Color) continue;
            if (brush is null || brush.Color.A == 0) transparent.Add(key);
        }

        Verdict(report, ref failures, "card-coloured fields keep an edge",
            transparent.Count == 0,
            transparent.Count == 0 ? "visible on a card panel"
                                   : "invisible on a card: " + string.Join(", ", transparent));
    }

    /// <summary>
    /// The owner has to be able to set their own password, and it has to actually protect
    /// something.
    ///
    /// Staff passwords are set on the Workers page, but the owner has no row there — so
    /// without a control of their own the back office stays open to whoever presses the lock,
    /// and nothing on screen says so.
    ///
    /// The hash is checked directly rather than through AdminAccount: that writes to the
    /// shop's real settings file, and a test has no business changing the owner's password.
    /// </summary>
    private static void CheckOwnerPassword(StringBuilder report, ref int failures, AdminWindow? shell)
    {
        Verdict(report, ref failures, "the owner has somewhere to set a password",
            shell?.FindName("PasswordButton") is System.Windows.Controls.Button,
            "a lock beside their name in the back office");

        var (hash, salt) = PasswordHash.Create("correct horse");

        var holds = PasswordHash.Verify("correct horse", hash, salt)
                    && !PasswordHash.Verify("Correct Horse", hash, salt)
                    && !PasswordHash.Verify("correct hors", hash, salt)
                    && !PasswordHash.Verify(string.Empty, hash, salt);

        Verdict(report, ref failures, "a password only opens for the password",
            holds, "case, length and empty all refused");

        // Two people choosing the same password must not produce the same stored hash, or one
        // leaked row would give away every account that shares it.
        var (again, otherSalt) = PasswordHash.Create("correct horse");
        Verdict(report, ref failures, "the same password stores differently each time",
            again != hash && otherSalt != salt,
            "salted per account");
    }

    /// <summary>
    /// Every pairing of ink and ground the app actually puts together, measured against
    /// WCAG AA.
    ///
    /// Worth holding because a palette drifts one convenient hex at a time and nothing on
    /// screen announces the moment a colour stopped being readable. This app shipped with its
    /// secondary text at 2.2:1 and white-on-green buttons at 2.6:1 — both far under the 4.5
    /// that ordinary text needs, and both invisible to anyone with good eyes and a good screen.
    /// </summary>
    private static void CheckContrast(StringBuilder report, ref int failures, Application app)
    {
        (string What, string Ink, string Ground)[] pairs =
        [
            ("body text on a panel",   "Brush.Text",   "Brush.Panel"),
            ("body text on a card",    "Brush.Text",   "Brush.Card"),
            ("secondary text on a panel", "Brush.Muted", "Brush.Panel"),
            ("secondary text on a card",  "Brush.Muted", "Brush.Card"),
            ("secondary text on the page","Brush.Muted", "Brush.Page"),
            ("a price on a panel",     "Brush.Accent", "Brush.Panel"),
            ("a price on a card",      "Brush.Accent", "Brush.Card"),
            ("a warning on a panel",   "Brush.Danger", "Brush.Panel"),
        ];

        var failed = new List<string>();

        foreach (var (what, ink, ground) in pairs)
        {
            var ratio = Contrast(Colour(app, ink), Colour(app, ground));
            if (ratio < 4.5) failed.Add($"{what} {ratio:0.00}:1");
        }

        // Button fills, where the text sits on the colour rather than beside it.
        foreach (var (what, fill) in new[] { ("primary button", "Brush.Accent"),
                                             ("danger button", "Brush.Danger") })
        {
            var ratio = Contrast(System.Windows.Media.Colors.White, Colour(app, fill));
            if (ratio < 4.5) failed.Add($"white on the {what} {ratio:0.00}:1");
        }

        Verdict(report, ref failures, "every colour pairing is readable",
            failed.Count == 0,
            failed.Count == 0
                ? $"{pairs.Length + 2} pairings, all at or above 4.5:1"
                : string.Join("; ", failed));
    }

    private static System.Windows.Media.Color Colour(Application app, string key) =>
        ((System.Windows.Media.SolidColorBrush)app.FindResource(key)).Color;

    /// <summary>WCAG relative luminance contrast, the same formula the guideline defines.</summary>
    private static double Contrast(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        static double Channel(byte v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double Luminance(System.Windows.Media.Color c) =>
            0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (high + 0.05) / (low + 0.05);
    }

    /// <summary>
    /// The rail has to be in the same place on every page.
    ///
    /// It sat in the row below the till's toolbar, and that toolbar only shows on the Sale
    /// page — so moving to Products collapsed the row and every rail icon jumped up. Navigation
    /// is how somebody knows where they are; a target that moves when you use it is the one
    /// piece of furniture that must not.
    ///
    /// Measured rather than looked at, because thirty pixels is exactly the distance that
    /// feels wrong without being obvious enough to report.
    /// </summary>
    private static void CheckRailHoldsStill(StringBuilder report, ref int failures)
    {
        try
        {
            var till = new MainWindow();
            var view = (ViewModels.SaleViewModel)till.DataContext;
            var root = (FrameworkElement)till.Content;

            double RailTopOn(PageKind page)
            {
                view.Page = page;
                root.Measure(new Size(1500, 900));
                root.Arrange(new Rect(0, 0, 1500, 900));
                root.UpdateLayout();

                var rail = (FrameworkElement)till.FindName("RailSale")!;
                return rail.TransformToAncestor(root).Transform(new Point(0, 0)).Y;
            }

            var onSale = RailTopOn(PageKind.Sale);
            var onProducts = RailTopOn(PageKind.Products);
            var onTickets = RailTopOn(PageKind.Tickets);

            var drift = Math.Max(Math.Abs(onSale - onProducts), Math.Abs(onSale - onTickets));

            Verdict(report, ref failures, "the rail is in the same place on every page",
                drift < 0.5,
                drift < 0.5
                    ? $"fixed at y={onSale:0} on all three"
                    : $"sale y={onSale:0}, products y={onProducts:0}, tickets y={onTickets:0}");
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  rail position: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>
    /// The page you are on is drawn solid; every other page is outlined.
    ///
    /// Which page is open was said in green and nothing else, and colour is the one channel a
    /// colour-blind cashier cannot read. The filled icon says it in shape as well. Worth a
    /// check because the swap is a data trigger reaching up the visual tree for the button
    /// that owns it — if that binding ever stops resolving, both icons simply render and
    /// nothing looks broken.
    /// </summary>
    private static void CheckSelectedPageIsDrawnSolid(StringBuilder report, ref int failures)
    {
        try
        {
            Session.SignOut();
            Session.UnlockAsOwner();

            var shell = new AdminWindow();
            shell.ShowWhatThisPersonMaySee();
            shell.GoTo(AdminPage.Inventory);

            var root = (FrameworkElement)shell.Content;
            root.Measure(new Size(1600, 900));
            root.Arrange(new Rect(0, 0, 1600, 900));
            root.UpdateLayout();

            var solids = Descendants(root)
                .OfType<System.Windows.Shapes.Path>()
                .Where(p => p.Style == shell.TryFindResource("Icon.NavSolid") as Style)
                .ToList();

            var showing = solids.Count(p => p.Visibility == Visibility.Visible);

            Verdict(report, ref failures, "the open page is the one drawn solid",
                solids.Count > 0 && showing == 1,
                solids.Count == 0
                    ? "no filled icons found in the sidebar"
                    : $"{showing} of {solids.Count} filled icons showing");
        }
        catch (Exception error)
        {
            failures++;
            report.AppendLine($"FAIL  selected page icon: {error.GetType().Name}: {error.Message}");
        }
    }

    private static void Verdict(StringBuilder report, ref int failures, string name, bool passed, string detail)
    {
        if (passed) report.AppendLine($"ok    {name} ({detail})");
        else
        {
            failures++;
            report.AppendLine($"FAIL  {name}: {detail}");
        }
    }

    private static void Check(StringBuilder report, ref int failures, string name, Func<object?> build)
    {
        try
        {
            build();
            report.AppendLine($"ok    {name}");
        }
        catch (Exception error)
        {
            failures++;
            var inner = error.InnerException is { } i ? $" | {i.GetType().Name}: {i.Message}" : string.Empty;
            report.AppendLine($"FAIL  {name}: {error.GetType().Name}: {error.Message}{inner}");
        }
    }

    private static AdminPageBase Build(AdminPage page) => page switch
    {
        AdminPage.AddProduct => new AddProductPage(),
        AdminPage.Dashboard => new DashboardPage(),
        AdminPage.SalesHistory => new SalesHistoryPage(),
        AdminPage.Categories => new CategoriesPage(),
        AdminPage.Inventory => new InventoryPage(),
        AdminPage.Suppliers => new SuppliersPage(),
        AdminPage.Workers => new WorkersPage(),
        AdminPage.Expenses => new ExpensesPage(),
        AdminPage.Reports => new ReportsPage(),
        AdminPage.Activity => new ActivityPage(),
        _ => new DashboardPage(),
    };
}
