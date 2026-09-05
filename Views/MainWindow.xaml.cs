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

        // A borderless window with WindowState=Maximized overhangs the screen by the
        // invisible resize border (~8px per side), which pushed the top-right window
        // controls partly off-screen and swallowed their clicks. Size to the work area
        // by hand instead — fills the screen, stays clear of the taskbar, nothing clipped.
        var work = SystemParameters.WorkArea;
        Left = work.Left;
        Top = work.Top;
        Width = work.Width;
        Height = work.Height;

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
        PreviewMouseDown += Window_PreviewMouseDown;
        PreviewKeyDown += Window_PreviewKeyDown;

        Vm.RequestBarcodeFocus += (_, _) => FocusBarcode();
        Vm.PaymentRequested += Vm_PaymentRequested;
        Vm.CartLineTouched += Vm_CartLineTouched;

        StartTalkingToTheBackOffice();
    }

    // ---------- The shop's network ----------

    private DispatcherTimer? _sync;

    /// <summary>
    /// Keeps this till and the back office in step, when there is a back office to keep step
    /// with. A shop with one computer never enters any of this.
    ///
    /// Everything here is best-effort on purpose: the till sells from its own database, so a
    /// failed exchange is a message in the corner of the screen, never an interruption.
    /// </summary>
    private void StartTalkingToTheBackOffice()
    {
        if (!ShopLink.IsConfigured) return;

        LinkChip.Visibility = Visibility.Visible;
        ShowLinkState();

        EventHandler linkChanged = (_, _) => Dispatcher.BeginInvoke(ShowLinkState);
        ShopLink.Changed += linkChanged;
        Closed += (_, _) => ShopLink.Changed -= linkChanged;

        // Half a minute. Often enough that a shop that came back online catches up while the
        // cashier is still serving the next customer, rare enough to be free.
        _sync = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _sync.Tick += (_, _) => _ = ShopLink.Sync();
        _sync.Start();
        Closed += (_, _) => _sync?.Stop();

        Loaded += (_, _) => _ = ShopLink.Sync();
    }

    private void ShowLinkState()
    {
        LinkStatus.Text = ShopLink.Status;
        LinkDot.Fill = (System.Windows.Media.Brush)FindResource(
            ShopLink.IsOnline ? "Brush.Accent" : "Brush.Danger");
        LinkChip.ToolTip = ShopLink.IsOnline
            ? $"Connected to {ShopLink.ShopName}. Press to send now."
            : $"{ShopLink.LastProblem} Press to try again.";
    }

    /// <summary>
    /// A cashier who can see something is wrong should be able to do the obvious thing about
    /// it without finding a settings screen.
    /// </summary>
    private async void LinkChip_Click(object sender, RoutedEventArgs e)
    {
        LinkStatus.Text = Loc.T("Sending…");
        await ShopLink.Sync();
        ShowLinkState();
        FocusBarcode();
    }

    /// <summary>
    /// Brings the just-scanned cart line into view. Runs at Background priority because the
    /// container for a brand new row does not exist until after the layout pass.
    /// </summary>
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
        AddedToastSubtitle.Text = $"{Vm.ItemCountLabel}  ·  {Vm.Total:N2} DH";

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
            new AdminWindow { Owner = this }.ShowDialog();

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

    private void CategoryBack_Click(object sender, RoutedEventArgs e)
    {
        Vm.CloseCategoryProducts();
        FocusBarcode();
    }

    /// <summary>Open a past ticket: preview it, and reprint from there if the customer wants a copy.</summary>
    private void Ticket_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int invoiceNumber }) return;

        var receipt = SaleRepository.FindByInvoiceNumber(invoiceNumber);
        if (receipt is null) return;

        new ReceiptWindow(receipt, allowReprint: true) { Owner = this }.ShowDialog();
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
        new ReprintWindow { Owner = this }.ShowDialog();
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

        ConfirmDetail.Text = $"Ticket #{Vm.LastInvoiceNumber}  ·  {total:N2} DH";

        // Print before the animation so paper starts moving immediately; any failure is
        // reported in the confirmation line rather than stopping the till, because the sale
        // is already banked by this point.
        if (AppSettings.Current.AutoPrintReceipts)
        {
            var receipt = SaleRepository.FindByInvoiceNumber(Vm.LastInvoiceNumber);
            if (receipt is not null)
            {
                // Never silently routes to Print-to-PDF: PrintSilent refuses virtual printers
                // and says so, rather than throwing a Save-As box at the cashier mid-queue.
                var error = ReceiptPrinter.PrintSilent(receipt, isDuplicate: false);
                if (error is not null) ConfirmDetail.Text = error;
            }
        }

        ((Storyboard)FindResource("PaymentConfirmed")).Begin(this);

        FocusBarcode();
    }

    // Custom window controls (top-right) — the window has no native title bar.
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => CloseApp("Close the app?");

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
