using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MarketPos.Converters;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;
using MarketPos.ViewModels;

namespace MarketPos.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Internal rather than private: the diagnostics drive the till directly to photograph
    /// states that only exist after a scan.
    /// </summary>
    internal SaleViewModel Vm => (SaleViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();

        // Translated here as well as on load. Waiting for an event is what left the sidebar in
        // English while every other part of this same window was Arabic.
        Services.Localizer.Apply(this);

        // The whole till scales to the screen it is on: rail, header, product grid and cart
        // together, as one piece. 1100x700 is the smallest it is genuinely usable at - three
        // product tiles beside a full cart - and above that size nothing happens at all.
        Services.Responsive.Shell(this, 1100, 700);

        // Opens filling the screen, which is right for a till and was wrong as the only
        // thing it could ever do: there was no way to move it and no way to make it smaller.
        // See Chrome for why this is not WindowState.Maximized.
        Chrome.Fill(this);
        ShowWindowSize();

        // Session is static, so the handler outlives the window unless it is taken off again.
        EventHandler sessionChanged = (_, _) => UpdateSignInUi();
        Session.Changed += sessionChanged;
        Closed += (_, _) => Session.Changed -= sessionChanged;
        UpdateSignInUi();

        // The empty panel answers two different questions, so it has to know which one is
        // being asked. Recomputed on every catalogue change, not just at start-up: the shop
        // stops being empty the moment the first product is added in the back office.
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Vm.IsShopEmpty) or nameof(Vm.HasVisibleProducts)
                                or nameof(Vm.IsEverythingScannable))
                UpdateEmptyState();
        };
        UpdateEmptyState();

        Loaded += (_, _) => FocusBarcode();
        Activated += (_, _) => FocusBarcode();

        // The on-screen keyboard, for a till with no keyboard on the counter. Started here
        // rather than in App, because the diagnostics build this window without ever showing
        // it — and a floating keyboard raised by a headless run is a window nobody asked for
        // that nothing is left to close.
        Loaded += (_, _) => TouchKeyboard.Start();
        Closed += (_, _) => TouchKeyboard.Stop();
        PreviewMouseDown += Window_PreviewMouseDown;
        PreviewKeyDown += Window_PreviewKeyDown;

        Vm.RequestBarcodeFocus += (_, _) => FocusBarcode();
        Vm.PaymentRequested += Vm_PaymentRequested;
        Vm.CartLineTouched += Vm_CartLineTouched;
        Vm.ScannedSomethingUnknown += Vm_ScannedSomethingUnknown;

        StartTalkingToTheBackOffice();
    }

    // ---------- The shop's network ----------

    private DispatcherTimer? _sync;

    /// <summary>
    /// Connects this till to the shop that owns it.
    ///
    /// The all-in-one MarketPos.exe is the shop and the till in one program and needs none of
    /// this. A till on another machine — MarketPosTill.exe — belongs to a server somewhere,
    /// and one that starts with no address would quietly keep its own books on the cashier's
    /// computer, which is the one place they must not be. So a till with no address searches
    /// the network for the shop, and asks the person setting it up where the shop is when
    /// it cannot be found.
    /// </summary>
    private void StartTalkingToTheBackOffice()
    {
        if (App.CurrentJob != App.Job.Till) return;

        LinkChip.Visibility = Visibility.Visible;

        // Empty shelves, every single time it opens.
        //
        // A till is a window onto the shop's database, not a shop. The file it keeps its copy
        // in is an ordinary database on an ordinary laptop: the app may have been run there as
        // a shop of its own, a database may have been carried over, or the copy may simply be
        // out of date. Any of those and the till shows products — its own, in green, looking
        // exactly like the shop's — and sells them into books that do not exist.
        //
        // So nothing carries over between openings. What is on this screen came from the
        // server this time, or it is not on this screen. If the server cannot be reached the
        // till says so in red and has nothing to sell, which is the truth: this machine does
        // not have a shop on it.
        // Nothing is done to this machine's database, because nothing on this machine's
        // database is ever read. A till's catalogue lives in memory, put there by the shop's
        // answer and gone when the app closes.

        if (!ShopLink.IsConfigured)
        {
            // The search runs once the till's own window is up, so the setup question it may
            // end in is asked in front of the till it belongs to. A till that is already
            // connected by then skips straight to the usual syncing.
            ShowLinkState();
            Loaded += async (_, _) => await FindTheShop();
            return;
        }

        SetupSyncing();
        Loaded += async (_, _) =>
        {
            await ShopLink.Sync();
            Vm.ReloadProducts();
        };
    }

    /// <summary>
    /// Finds the shop a till with no address belongs to, and either connects it or asks
    /// where the shop is. Called once, when the till's window is already on screen.
    /// </summary>
    private async System.Threading.Tasks.Task FindTheShop()
    {
        // An address that works is the end of it.
        //
        // A till now starts out pointed at pos-server, which is right when the shop's server
        // machine carries that name and useless when it does not -- and "configured" used to be
        // enough to stop the search below from ever running. So the address is tried, and only
        // an address that actually answers counts as configured.
        if (ShopLink.IsConfigured)
        {
            SetupSyncing();
            await ShopLink.Sync();

            if (ShopLink.IsOnline)
            {
                Vm.ReloadProducts();
                return;
            }
        }

        ShowLooking();

        var found = await ShopFinder.Look();

        // Never itself. A machine that answers from this very computer is not the shop this
        // till belongs to — it is something serving on this laptop, and connecting to it makes
        // the till a mirror of its own database while the chip says, truthfully and uselessly,
        // that it is connected. That is the exact shape of "he opened it and saw his own
        // products": connected, in green, to himself.
        if (found is not null && ShopFinder.IsThisMachine(found.Address)) found = null;

        if (found is not null)
        {
            AppSettings.Current.ServerAddress = found.Address;
            AppSettings.Current.Save();
        }

        if (ShopLink.IsConfigured && await ShopLink.Ping())
        {
            await ShopLink.Sync();
            Vm.ReloadProducts();
            SetupSyncing();
            return;
        }

        // No shop answered. The person setting the till up can type the address or press
        // Find again; choosing to work alone leaves the till visibly unconnected in red,
        // rather than silently running a second shop that nobody knows about.
        if (ServerSetupWindow.Ask(this, out _))
        {
            await ShopLink.Sync();
            Vm.ReloadProducts();
            SetupSyncing();
        }
        else
        {
            ShowLinkState();
        }
    }

    /// <summary>
    /// The steady-state conversation with the back office: a half-minute watch, an indicator
    /// that follows it, and nothing that can interrupt a sale. Everything here is best-effort
    /// on purpose — the till sells from its own database, so a failed exchange is a message in
    /// the corner of the screen, never an interruption.
    /// </summary>
    private void SetupSyncing()
    {
        EventHandler linkChanged = (_, _) => Dispatcher.BeginInvoke(ShowLinkState);
        ShopLink.Changed += linkChanged;
        Closed += (_, _) => ShopLink.Changed -= linkChanged;

        // Half a minute. Often enough that a shop that came back online catches up while the
        // cashier is still serving the next customer, rare enough to be free.
        _sync = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _sync.Tick += (_, _) => _ = ShopLink.Sync();
        _sync.Start();
        Closed += (_, _) => _sync?.Stop();

        ShowLinkState();
    }

    private void ShowLinkState()
    {
        // A till with no shop to belong to must look nothing like a till that has one.
        // This is the state the previous builds never showed: it is the honest face of a
        // till that would otherwise be a second, hidden shop.
        if (!ShopLink.IsConfigured)
        {
            LinkStatus.Text = Loc.T("Not connected to the shop");
            LinkDot.Fill = (System.Windows.Media.Brush)FindResource("Brush.Danger");
            LinkChip.ToolTip = Loc.T("Press to connect this till to the shop's server.");
            return;
        }

        LinkStatus.Text = ShopLink.Status;
        LinkDot.Fill = (System.Windows.Media.Brush)FindResource(
            ShopLink.IsOnline ? "Brush.Accent" : "Brush.Danger");

        // The address, not just the word "connected". Which machine a till is talking to is
        // the one thing that goes wrong when two computers are set up, and it was the one
        // thing the chip would not say.
        LinkChip.ToolTip = ShopLink.IsOnline
            ? Loc.T("Connected to {0} at {1}. Press to send now.",
                    ShopLink.ShopName, Loc.Ltr(ShopLink.Address))
            : $"{ShopLink.LastProblem} Press to try again.";
    }

    private void ShowLooking()
    {
        LinkStatus.Text = Loc.T("Looking for the shop…");
        LinkDot.Fill = (System.Windows.Media.Brush)FindResource("Brush.Danger");
        LinkChip.ToolTip = Loc.T("Searching this network for the shop's server. This takes a moment.");
    }

    /// <summary>
    /// A cashier who can see something is wrong should be able to do the obvious thing about
    /// it without finding a settings screen.
    /// </summary>
    private async void LinkChip_Click(object sender, RoutedEventArgs e)
    {
        LinkChip.IsEnabled = false;

        try
        {
            // Already talking to the shop: this is the "send what is waiting" button, and
            // saying so is the whole of its job.
            if (ShopLink.IsConfigured && ShopLink.IsOnline)
            {
                LinkStatus.Text = Loc.T("Sending…");
                await ShopLink.Sync();
                return;
            }

            // Not talking to the shop. A till starts out pointed at pos-server, so being
            // unconnected almost always means that machine is off, is called something else,
            // or is on another network -- and the cashier pressing this needs it either fixed
            // or explained, not a chip that flickers and says the same thing again.
            LinkStatus.Text = Loc.T("Sending…");
            await ShopLink.Sync();
            if (ShopLink.IsOnline)
            {
                Vm.ReloadProducts();
                SetupSyncing();
                return;
            }

            ShowLooking();
            var found = await ShopFinder.Look();
            if (found is not null && ShopFinder.IsThisMachine(found.Address)) found = null;

            if (found is not null)
            {
                AppSettings.Current.ServerAddress = found.Address;
                AppSettings.Current.Save();

                await ShopLink.Sync();
                if (ShopLink.IsOnline)
                {
                    Vm.ReloadProducts();
                    SetupSyncing();
                    return;
                }
            }

            // Nothing answered by name and nothing answered on this network. The address is
            // the thing to correct, so the screen that corrects it is what opens.
            if (ServerSetupWindow.Ask(this, out _))
            {
                await ShopLink.Sync();
                Vm.ReloadProducts();
                SetupSyncing();
            }
        }
        finally
        {
            LinkChip.IsEnabled = true;
            ShowLinkState();
            FocusBarcode();
        }
    }

    /// <summary>
    /// Brings the just-scanned cart line into view. Runs at Background priority because the
    /// container for a brand new row does not exist until after the layout pass.
    /// </summary>
    /// <summary>
    /// Something was scanned that the shop does not sell. Offers to add it, there and then.
    ///
    /// <para>
    /// This is the one moment anybody knows what the thing is: it is in the cashier's hand,
    /// the price is on the box, and the delivery it came out of is on the floor beside them.
    /// The alternative was a red line saying "not found", a note on paper, and somebody in the
    /// back office that evening working out what 6111245830021 was.
    /// </para>
    ///
    /// <para>
    /// It asks first, because a scan that finds nothing is often a scan of the wrong thing —
    /// a loyalty card, a customer's own shopping, a barcode on the shelf edge — and a form
    /// opening by itself in the middle of a queue is worse than the red line was. Saying yes
    /// opens the same form the back office uses, with the barcode already in it, and what it
    /// saves goes into the shop's own database exactly as it would from Inventory.
    /// </para>
    /// </summary>
    private void Vm_ScannedSomethingUnknown(object? sender, string barcode)
    {
        // Whoever is at the till has to be allowed to put something in the books. A cashier
        // without that right gets the red line and nothing else, which is the state the till
        // was in before this existed.
        if (!Session.Can(Permission.ManageProducts)) return;

        // And there has to be a till on screen to ask. The diagnostics build this window
        // without ever showing it and then scan things through it, which is exactly what they
        // are for; a modal question raised there is asked of nobody and answered by nobody,
        // and it would hold the run open for ever waiting.
        if (!Owned.CanOwn(this)) return;

        if (!ConfirmWindow.Ask(this,
                Loc.T("{0} is not in the shop yet. Add it?", Loc.Ltr(barcode)),
                Loc.T("You will be asked for its name and price. It goes on the till as soon "
                    + "as you save, and this sale can carry on.")))
        {
            FocusBarcode();
            return;
        }

        if (Views.Admin.ProductWindow.AddScanned(this, barcode))
        {
            // Straight onto the sale it interrupted. The cashier scanned it because a customer
            // is buying it.
            Catalog.Reload();
            Vm.ReloadProducts();
            Vm.SearchText = barcode;
            Vm.SubmitBarcodeCommand.Execute(null);
        }

        FocusBarcode();
    }

    private void Vm_CartLineTouched(object? sender, CartLine line)
    {
        // Off the Sale page the cart sidebar does not exist, so a toast is the only
        // confirmation the cashier gets that the tap registered.
        if (!Vm.IsSalePage)
        {
            ShowAddedToast(line);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            CartList.UpdateLayout();
            if (CartList.ItemContainerGenerator.ContainerFromItem(line) is FrameworkElement row)
                row.BringIntoView();
        }));
    }

    private void ShowAddedToast(CartLine line)
    {
        AddedToastTitle.Text = line.Product.Name;
        AddedToastSubtitle.Text = $"{Vm.ItemCountLabel}  ·  {Loc.Ltr($"{Vm.Total:N2} DH")}";

        AddedToastImage.Source = string.IsNullOrWhiteSpace(line.Product.ImagePath)
            ? null
            : new ImagePathConverter().Convert(line.Product.ImagePath, typeof(object), null,
                                               System.Globalization.CultureInfo.CurrentCulture) as ImageSource;

        ((Storyboard)FindResource("ProductAdded")).Begin(this);
    }

    // ---------- Keyboard shortcuts ----------
    //
    // F3 recalls the last held ticket and Ctrl+Z undoes the last scan. Esc is not grabbed
    // globally, because the quantity editor uses it to cancel an edit — it only reaches us
    // when the search box has focus.

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F3:
                Execute(Vm.ResumeLastTicketCommand);
                e.Handled = true;
                break;

            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                Execute(Vm.RemoveLastLineCommand);
                e.Handled = true;
                break;

            case Key.Escape:
                if (IsEditingQuantity()) return;   // let the quantity box cancel its own edit
                e.Handled = HandleEscape();
                break;
        }
    }

    private static void Execute(RelayCommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private bool IsEditingQuantity() =>
        Keyboard.FocusedElement is TextBox box && !ReferenceEquals(box, BarcodeBox);

    /// <summary>Esc backs out of whatever is in the way, one layer at a time.</summary>
    private bool HandleEscape()
    {
        // The price card is the top layer while it is up, and the one most likely to be in
        // the way of the next customer.
        if (Vm.HasPriceCheckResult)
        {
            Vm.ClearPriceCheck();
            return true;
        }

        if (Vm.IsPriceCheck)
        {
            Vm.IsPriceCheck = false;
            return true;
        }

        if (Vm.SearchText.Length > 0)
        {
            Vm.SearchText = string.Empty;
            FocusBarcode();
            return true;
        }

        if (Vm.HasItems)
        {
            CancelSale_Click(this, new RoutedEventArgs());
            return true;
        }

        return false;
    }

    private void ClosePriceCheck_Click(object sender, RoutedEventArgs e) => Vm.ClearPriceCheck();

    // ---------- Editable quantity ----------

    private void QuantityBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Select everything so the cashier can just type over it.
        if (sender is TextBox box)
            box.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(box.SelectAll));
    }

    private void QuantityBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox box) return;

        var proposed = box.Text.Remove(box.SelectionStart, box.SelectionLength)
                               .Insert(box.SelectionStart, e.Text);

        // Digits, and at most one decimal separator — only where fractions make sense.
        var allowsFraction = (box.DataContext as CartLine)?.Product.Unit == Unit.Kg;
        e.Handled = !QuantityPattern(allowsFraction).IsMatch(proposed);
    }

    private static Regex QuantityPattern(bool allowsFraction) =>
        allowsFraction ? WeightInput : WholeInput;

    private static readonly Regex WeightInput = new(@"^\d*([.,]\d{0,3})?$", RegexOptions.Compiled);
    private static readonly Regex WholeInput = new(@"^\d*$", RegexOptions.Compiled);

    private void QuantityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        switch (e.Key)
        {
            case Key.Enter:
                Commit(box);
                FocusBarcode();          // straight back to scanning
                e.Handled = true;
                break;

            case Key.Escape:
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();  // discard the edit
                FocusBarcode();
                e.Handled = true;
                break;
        }
    }

    private static void Commit(TextBox box) =>
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    // Every button in the app is Focusable="False" so clicks never disturb the scanner —
    // which means clicking away from a quantity box would otherwise leave focus (and the
    // next scan, and the uncommitted edit) stranded in it. Commit and hand focus back.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox box || ReferenceEquals(box, BarcodeBox)) return;
        if (e.OriginalSource is DependencyObject clicked && IsWithin(clicked, box)) return;

        Commit(box);
        FocusBarcode();
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    // --- Key gotcha: the scanner is just a keyboard, so the barcode box must stay
    // focused. Every path that could move focus away routes back through here. ---

    // Runs at Background priority, below input: never reclaim focus mid-click, or the
    // button we stole it from loses mouse capture and its Click never fires.
    private void BarcodeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!IsActive) return;                                   // a modal dialog owns focus right now
            if (Keyboard.FocusedElement is TextBox tb && tb != BarcodeBox) return; // another field wants typed input
            FocusBarcode();
        }));
    }

    private void FocusBarcode()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            BarcodeBox.Focus();
            Keyboard.Focus(BarcodeBox);
            BarcodeBox.SelectAll();
        }));
    }

    // ---------- Navigation ----------

    private void Nav_Sale(object sender, RoutedEventArgs e) => GoTo(PageKind.Sale);
    private void Nav_Products(object sender, RoutedEventArgs e) => GoTo(PageKind.Products);
    private void Nav_Tickets(object sender, RoutedEventArgs e) => GoTo(PageKind.Tickets);

    /// <summary>
    /// Admin is password-gated. Unlocking lasts until the till is closed, so the owner is not
    /// retyping the password every time they step away from the printer settings.
    /// </summary>
    private void Nav_Admin(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        if (StaffSignInWindow.Ask(this))
        {
            // The back office is its own window rather than a fourth page in the till. The
            // till stays a single-purpose screen that a cashier cannot get lost in, and the
            // office gets the width its tables need.
            new AdminWindow().By(this).ShowDialog();

            Catalog.Reload();
            Vm.ReloadProducts();
            RestoreRailSelection();
            FocusBarcode();
            return;
        }

        // Refused. The rail button checked itself on click, so put the selection back on
        // whichever page is actually on screen.
        RestoreRailSelection();
        FocusBarcode();
    }

    /// <summary>
    /// Puts the rail back on the page actually showing. A rail button checks itself the moment
    /// it is clicked, so a refused gate would otherwise leave it lit over a screen nobody
    /// reached.
    /// </summary>
    private void RestoreRailSelection()
    {
        RailAdmin.IsChecked = false;

        var button = Vm.Page switch
        {
            PageKind.Products => RailProducts,
            PageKind.Tickets  => RailTickets,
            _                 => RailSale,
        };
        button.IsChecked = true;
    }

    private void GoTo(PageKind page)
    {
        if (!IsLoaded) return;   // the rail raises Checked while the window is still building
        Vm.Page = page;
    }

    /// <summary>Drill into a category to see what is inside it.</summary>
    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string category })
            Vm.OpenCategoryProducts(category);
        FocusBarcode();
    }

    /// <summary>
    /// Sets a weighed line to an amount the cashier pressed rather than typed.
    ///
    /// Only ever reaches a line already on the sale, and only changes how much of it there is
    /// — the price per kilo is the product's and is not touched, so the line total follows
    /// from the weight the way it does when the figure is typed.
    /// </summary>
    private void Weight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag, DataContext: CartLine line }) return;
        if (!decimal.TryParse(tag, NumberStyles.Number, CultureInfo.InvariantCulture, out var kilos)) return;

        line.Quantity = kilos;
        Vm.RefreshTotals();
        FocusBarcode();
    }

    private void CategoryBack_Click(object sender, RoutedEventArgs e)
    {
        Vm.CloseCategoryProducts();
        FocusBarcode();
    }

    /// <summary>Open a past ticket: preview it, and reprint from there if the customer wants a copy.</summary>
    private void Ticket_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int invoiceNumber }) return;

        var receipt = Receipts.Find(invoiceNumber);
        if (receipt is null) return;

        new ReceiptWindow(receipt, allowReprint: true).By(this).ShowDialog();
        FocusBarcode();
    }

    private void Discount_Click(object sender, RoutedEventArgs e)
    {
        if (DiscountWindow.Ask(this, Vm.GrossBeforeDiscount, Vm.DiscountKind, Vm.DiscountValue,
                               out var kind, out var value))
        {
            Vm.ApplyDiscount(kind, value);
        }
        FocusBarcode();
    }

    private void Reprint_Click(object sender, RoutedEventArgs e)
    {
        new ReprintWindow().By(this).ShowDialog();
        FocusBarcode();
    }

    private void CancelSale_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmWindow.Ask(this, "Cancel this sale?", "The cart will be cleared."))
            Vm.ClearCart();

        FocusBarcode();
    }

    /// <summary>
    /// Pay goes straight through — no dialog. The sale is recorded first; only once it is
    /// safely in the database does the confirmation play and the cart clear, so a failed
    /// write leaves the basket intact instead of silently losing it.
    /// </summary>
    private void Vm_PaymentRequested(object? sender, decimal amountDue)
    {
        var total = Vm.Total;
        Vm.CompleteSale(Vm.PaymentMethod, total);

        if (Vm.LastInvoiceNumber <= 0) return;   // CompleteSale reported the failure already

        ConfirmDetail.Text = $"{Loc.T("Ticket #{0}", Vm.LastInvoiceNumber)}  ·  {Loc.Ltr($"{total:N2} DH")}";

        var paper = Receipts.Find(Vm.LastInvoiceNumber);

        // Print directly to the configured printer without asking.
        if (paper is not null)
        {
            var problem = ReceiptPrinter.PrintSilent(paper, isDuplicate: false);
            if (problem is not null) ConfirmDetail.Text = problem;
        }

        ((Storyboard)FindResource("PaymentConfirmed")).Begin(this);
        FocusBarcode();
    }

    // Custom window controls (top-right) — the window has no native title bar.
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>The middle window control: fill the screen, or come back off it.</summary>
    private void Size_Click(object sender, RoutedEventArgs e)
    {
        Chrome.Toggle(this);
        ShowWindowSize();
    }

    /// <summary>The button says what pressing it will do, so it changes with the window.</summary>
    private void ShowWindowSize()
    {
        var filled = Chrome.FillsTheScreen(this);

        SizeGlyph.Data = (System.Windows.Media.Geometry)FindResource(
            filled ? "Icon.Restore" : "Icon.Maximize");
        SizeButton.ToolTip = Loc.T(filled ? "Make the window smaller" : "Fill the screen");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseApp("Close the app?");

    /// <summary>
    /// Settings, from the till.
    ///
    /// No permission check and no admin password. Settings holds the language and nothing else
    /// now — which is a display preference, not a business decision, and the person it matters
    /// most to is the cashier standing at this screen for eight hours. Everything that was
    /// worth protecting on this window is protected where it is done: the back office is behind
    /// its own password, and the repositories refuse a write nobody is allowed to make.
    ///
    /// Nothing to refresh afterwards. A language arrives when windows are built, so the dialog
    /// offers the restart itself and this window is gone by the time it matters.
    /// </summary>
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Ask(this);

    /// <summary>
    /// The power icon does whichever of the two things is actually on the table. With somebody
    /// signed in it signs them out, so the machine can be handed over without closing anything;
    /// with nobody signed in there is nothing to sign out of, so it closes the till. The window
    /// controls top-right still close the app either way.
    /// </summary>
    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (!SignedIn)
        {
            CloseApp("Close the till?");
            return;
        }

        var who = Session.CurrentName;
        if (!ConfirmWindow.Ask(this, $"Sign {who} out?",
                "The till keeps running. The back office will ask for a name and password again."))
        {
            FocusBarcode();
            return;
        }

        Session.SignOut();
        Vm.Announce($"{who} signed out");
        FocusBarcode();
    }

    /// <summary>
    /// What the empty grid should say. A shop with nothing in it is not a search with no
    /// results, and offering "Clear search" to somebody who has not searched is worse than
    /// saying nothing.
    /// </summary>
    private void UpdateEmptyState()
    {
        if (Vm.IsShopEmpty)
        {
            EmptyTitle.Text = Loc.T("Nothing in the shop yet");
            EmptyBody.Text = Loc.T("Products are added in the back office, under Add product. "
                                 + "Once they are in, they show up here and scan at the counter.");
            ClearSearchButton.Visibility = Visibility.Collapsed;
            return;
        }

        // A stocked shop where everything has a barcode. The grid being empty is the design
        // working, not a fault, and the cashier has to be told which it is.
        if (Vm.IsEverythingScannable)
        {
            EmptyTitle.Text = Loc.T("Scan it");
            EmptyBody.Text = Loc.T("Everything in the shop has a barcode, so there is nothing to press. "
                                 + "Bread, produce and anything else without one appears here.");
            ClearSearchButton.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyTitle.Text = Loc.T("No products found");
        EmptyBody.Text = Loc.T("Nothing here matches what you typed, or the category filter is hiding it.");
        ClearSearchButton.Visibility = Visibility.Visible;
    }

    /// <summary>Whether anyone is holding the back office open — the owner, or a worker.</summary>
    private static bool SignedIn => Session.Current is not null || Session.IsOwnerUnlocked;

    /// <summary>
    /// Keeps the power icon honest about what it will do, and puts the signed-in name on the
    /// lock. Somebody has to be able to see that a session is still open before they can think
    /// to close it.
    /// </summary>
    private void UpdateSignInUi()
    {
        SignOutButton.ToolTip = SignedIn
            ? Loc.T("Sign {0} out", Session.CurrentName)
            : Loc.T("Close the till");
        RailAdmin.ToolTip = SignedIn
            ? Loc.T("Back office — {0}", Session.CurrentName)
            : Loc.T("Back office");
    }

    // Nothing to lose with an empty cart, so don't nag — just close. Only an
    // in-progress sale is worth a confirmation.
    private void CloseApp(string prompt)
    {
        if (Vm.HasItems &&
            !ConfirmWindow.Ask(this, prompt, "The current sale will be discarded."))
        {
            FocusBarcode();
            return;
        }

        Application.Current.Shutdown();
    }
}
