using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.ViewModels;

public sealed class SaleViewModel : ViewModelBase
{
    public ObservableCollection<CartLine> Cart { get; } = new();

    /// <summary>Sales parked mid-scan, waiting to be picked back up.</summary>
    public ObservableCollection<HeldTicket> HeldTickets { get; } = new();

    public ObservableCollection<string> Categories { get; } = new(Catalog.Categories);
    public IReadOnlyList<string> SortOptions { get; } = new[] { "Name", "Price: Low to High", "Price: High to Low" };

    /// <summary>Backing list for the product grid; filtered and sorted live via a CollectionView.</summary>
    public ICollectionView ProductsView { get; private set; }

    private string _selectedCategory = "All";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value))
                RefreshProducts();
        }
    }

    private string _selectedSort = "Name";
    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetField(ref _selectedSort, value))
                ApplySort();
        }
    }

    /// <summary>
    /// The one input box: a scanner types a barcode into it and hits Enter, a cashier types
    /// part of a product name. Typing filters the grid live, which is the only practical way
    /// to find loose produce and bread — the items that have no barcode to scan.
    /// </summary>
    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value)) return;

            OnPropertyChanged(nameof(IsEverythingScannable));
            RefreshProducts();                           // Sale page grid
            if (IsProductsPage) RefreshProductsPage();   // categories, or the open category
            if (IsTicketsPage) LoadTickets();            // receipt number lookup
        }
    }

    // ---------- Price check ----------

    /// <summary>
    /// While this is on, a scan answers "what is this and what does it cost" instead of
    /// putting the item on the sale.
    ///
    /// The alternative shopkeepers fall back on — ring it up, read the line, void it — leaves
    /// voided sales all over the day's takings and, on a busy counter, sometimes does not get
    /// voided at all. This never writes anything.
    /// </summary>
    private bool _isPriceCheck;
    public bool IsPriceCheck
    {
        get => _isPriceCheck;
        set
        {
            if (!SetField(ref _isPriceCheck, value)) return;

            // Leaving the mode clears the answer with it: a price card left on screen while
            // the till is selling again is a price card somebody will read as the current item.
            PriceCheckResult = null;
            SearchText = string.Empty;
            StatusMessage = string.Empty;
            FocusBarcode();
        }
    }

    private PriceCheck? _priceCheckResult;
    public PriceCheck? PriceCheckResult
    {
        get => _priceCheckResult;
        private set
        {
            if (!SetField(ref _priceCheckResult, value)) return;
            OnPropertyChanged(nameof(HasPriceCheckResult));
        }
    }

    public bool HasPriceCheckResult => _priceCheckResult is not null;

    /// <summary>
    /// Looks a code up and shows the answer, without going through the input box. What the
    /// scan path calls, and what the diagnostics and the self-test drive directly.
    /// </summary>
    public void CheckPrice(string query) => PriceCheckResult = PriceCheck.For(query);

    /// <summary>Clears the card without leaving the mode — the cashier is ready for the next one.</summary>
    public void ClearPriceCheck()
    {
        PriceCheckResult = null;
        SearchText = string.Empty;
        FocusBarcode();
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    /// <summary>Drives the colour of the status banner — red for problems, green for confirmations.</summary>
    private bool _statusIsError;
    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetField(ref _statusIsError, value);
    }

    /// <summary>A plain confirmation in the till's status banner, from outside the view model.</summary>
    public void Announce(string message) => SetStatus(message, isError: false);

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    /// <summary>False when the current filter hides everything, so the grid can explain itself.</summary>
    public bool HasVisibleProducts => !ProductsView.IsEmpty;

    // ---------- Navigation ----------

    private PageKind _page = PageKind.Sale;
    public PageKind Page
    {
        get => _page;
        set
        {
            if (!SetField(ref _page, value)) return;
            OnPropertyChanged(nameof(IsSalePage));
            OnPropertyChanged(nameof(IsProductsPage));
            OnPropertyChanged(nameof(IsTicketsPage));
            OnPropertyChanged(nameof(IsSaleGridVisible));
            OnPropertyChanged(nameof(IsSaleEmptyVisible));
            OnPropertyChanged(nameof(IsCartPanelOpen));
            if (value == PageKind.Tickets)
            {
                _searchText = string.Empty;   // never land on a pre-filtered list
                OnPropertyChanged(nameof(SearchText));
                LoadTickets();
            }
            if (value == PageKind.Products) LoadCategories();
            else CloseCategoryProducts();
            FocusBarcode();
        }
    }

    public bool IsSalePage => Page == PageKind.Sale;

    /// <summary>Sale-page composites, so the XAML never has to combine two bindings itself.</summary>
    public bool IsSaleGridVisible => IsSalePage && HasVisibleProducts;
    public bool IsSaleEmptyVisible => IsSalePage && !HasVisibleProducts;

    /// <summary>
    /// The shop has nothing in it at all, as opposed to a search that matched nothing.
    ///
    /// Two different problems with two different answers. A new shop that opened the till and
    /// read "nothing here matches what you typed" would go looking for a broken search box,
    /// when what it actually needs is to be told where products come from.
    /// </summary>
    public bool IsShopEmpty => Catalog.Products.Count == 0;

    /// <summary>
    /// The shop is stocked, but every last thing in it has a barcode — so the grid is empty
    /// and that is correct.
    ///
    /// A third state, and the one that would otherwise look most like a fault: a cashier
    /// facing a blank screen in a full shop needs to be told to reach for the scanner, not
    /// left reading "no products found" and wondering what broke.
    /// </summary>
    public bool IsEverythingScannable =>
        !IsShopEmpty && !IsSearching
        && Catalog.Products.Where(p => p.SoldAtTheTill).All(p => p.IsScannable);
    public bool IsCartPanelOpen => IsSalePage && HasItems;
    public bool IsProductsPage => Page == PageKind.Products;
    public bool IsTicketsPage => Page == PageKind.Tickets;

    // ---------- Products page ----------

    /// <summary>
    /// The categories, shown as picture tiles. Opening one drills into its products; the
    /// full flat product list is on the Sale page, so repeating it here served no purpose.
    /// </summary>
    public ObservableCollection<CategorySummary> CategorySummaries { get; } = new();

    /// <summary>Products inside the category the cashier opened. Empty while the tiles are shown.</summary>
    public ObservableCollection<Product> CategoryProducts { get; } = new();

    private string? _openCategory;
    public string? OpenCategory
    {
        get => _openCategory;
        private set
        {
            if (!SetField(ref _openCategory, value)) return;
            OnPropertyChanged(nameof(IsCategoryOpen));
            OnPropertyChanged(nameof(IsCategoryListVisible));
        }
    }

    public bool IsCategoryOpen => OpenCategory is not null;
    public bool IsCategoryListVisible => OpenCategory is null;

    /// <summary>Rebuilds whichever Products-page list is on screen, filtered by the search box.</summary>
    private void RefreshProductsPage()
    {
        if (OpenCategory is null) LoadCategories();
        else LoadCategoryProducts(OpenCategory);
    }

    public void LoadCategories()
    {
        CategorySummaries.Clear();

        // Only categories that have something worth pressing in them. A category holding
        // nothing but scanned products opens onto an empty shelf.
        var groups = Catalog.Products
            .Where(p => p.SoldAtTheTill)
            .Where(p => !p.IsScannable || IsSearching)
            .GroupBy(p => p.Category)
            .Where(g => MatchesText(g.Key, SearchText))
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            CategorySummaries.Add(new CategorySummary
            {
                Name = group.Key,
                ImagePath = group.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ImagePath))?.ImagePath,
            });
        }

        OnPropertyChanged(nameof(HasProductsPageResults));
    }

    private void LoadCategoryProducts(string category)
    {
        CategoryProducts.Clear();

        var products = Catalog.Products
            .Where(p => p.Category == category)
            .Where(p => p.SoldAtTheTill)
            .Where(p => !p.IsScannable || IsSearching)
            .Where(p => Matches(p, SearchText))
            .OrderBy(p => p.Name);

        foreach (var product in products)
            CategoryProducts.Add(product);

        OnPropertyChanged(nameof(HasProductsPageResults));
    }

    /// <summary>Case-insensitive contains; an empty query matches everything.</summary>
    private static bool MatchesText(string value, string? query)
    {
        query = query?.Trim();
        return string.IsNullOrEmpty(query)
            || value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    public bool HasProductsPageResults =>
        OpenCategory is null ? CategorySummaries.Count > 0 : CategoryProducts.Count > 0;

    public void OpenCategoryProducts(string category)
    {
        // Drop the query when changing view, so a category never opens pre-filtered by
        // whatever was typed to find the category itself.
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));

        OpenCategory = category;
        LoadCategoryProducts(category);
    }

    public void CloseCategoryProducts()
    {
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));

        CategoryProducts.Clear();
        OpenCategory = null;
        LoadCategories();
    }

    // ---------- Tickets page ----------

    public ObservableCollection<SaleSummary> Tickets { get; } = new();

    private string _ticketsHeadline = string.Empty;
    /// <summary>"12 sales today  ·  1 430.00 DH" — the figure a cashier checks at close of shift.</summary>
    public string TicketsHeadline
    {
        get => _ticketsHeadline;
        private set => SetField(ref _ticketsHeadline, value);
    }

    public bool HasTickets => Tickets.Count > 0;

    public void LoadTickets()
    {
        Tickets.Clear();
        foreach (var sale in SaleRepository.ListSales(SearchText))
            Tickets.Add(sale);

        var (count, total) = SaleRepository.DayTotals(DateTime.Now);
        TicketsHeadline = count == 0
            ? "No sales today yet"
            : $"{count} {(count == 1 ? "sale" : "sales")} today  ·  {total:N2} DH";

        OnPropertyChanged(nameof(HasTickets));
    }

    // ---------- Remise (customer discount) ----------

    private DiscountKind _discountKind = DiscountKind.None;
    public DiscountKind DiscountKind
    {
        get => _discountKind;
        private set => SetField(ref _discountKind, value);
    }

    private decimal _discountValue;
    /// <summary>What the cashier typed: 10 meaning 10% or 10 DH depending on the kind.</summary>
    public decimal DiscountValue
    {
        get => _discountValue;
        private set => SetField(ref _discountValue, value);
    }

    public bool HasDiscount => DiscountKind != DiscountKind.None && DiscountAmount > 0;

    public string DiscountLabel => DiscountKind switch
    {
        DiscountKind.Percent => $"Remise ({DiscountValue:0.##}%)",
        DiscountKind.Fixed => $"Remise ({DiscountValue:0.00} DH)",
        _ => "Remise",
    };

    /// <summary>Everything in the cart at shelf price, before any remise.</summary>
    public decimal GrossBeforeDiscount => Math.Round(Cart.Sum(l => l.LineTotal), 2);

    /// <summary>Never more than the basket is worth — a remise cannot make the till owe money.</summary>
    public decimal DiscountAmount
    {
        get
        {
            if (DiscountKind == DiscountKind.None || DiscountValue <= 0) return 0m;
            var raw = DiscountKind == DiscountKind.Percent
                ? GrossBeforeDiscount * DiscountValue / 100m
                : DiscountValue;
            return Math.Round(Math.Clamp(raw, 0m, GrossBeforeDiscount), 2);
        }
    }

    public decimal Total => Math.Round(GrossBeforeDiscount - DiscountAmount, 2);

    /// <summary>
    /// VAT is scaled down with the remise. Shelf prices are tax-inclusive, so discounting the
    /// amount paid must discount the tax inside it too — otherwise the VAT declared would be
    /// higher than the VAT actually collected.
    /// </summary>
    public decimal Tax
    {
        get
        {
            var gross = GrossBeforeDiscount;
            if (gross <= 0) return 0m;
            var fullTax = Cart.Sum(l => l.LineTax);
            return Math.Round(fullTax * (Total / gross), 2);
        }
    }

    public decimal Subtotal => Math.Round(Total - Tax, 2);

    public void ApplyDiscount(DiscountKind kind, decimal value)
    {
        DiscountKind = kind;
        DiscountValue = value;
        RaiseTotalsChanged();
        FocusBarcode();
    }

    public void ClearDiscount() => ApplyDiscount(DiscountKind.None, 0m);
    public bool HasItems => Cart.Count > 0;
    public bool HasHeldTickets => HeldTickets.Count > 0;

    /// <summary>Distinct lines, not units — "3 items" alongside a 2.4 kg line reads better than "5.4".</summary>
    public int ItemCount => Cart.Count;
    public string ItemCountLabel => ItemCount == 1 ? "1 item" : $"{ItemCount} items";

    public RelayCommand SubmitBarcodeCommand { get; }
    public RelayCommand AddProductCommand { get; }
    public RelayCommand IncrementCommand { get; }
    public RelayCommand DecrementCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public RelayCommand PayCommand { get; }
    public RelayCommand HoldSaleCommand { get; }
    public RelayCommand ResumeTicketCommand { get; }
    public RelayCommand DiscardTicketCommand { get; }
    public RelayCommand ResumeLastTicketCommand { get; }
    public RelayCommand RemoveLastLineCommand { get; }
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Raised whenever the UI should snap keyboard focus back to the barcode box.</summary>
    public event EventHandler? RequestBarcodeFocus;

    /// <summary>Raised when Pay is pressed with a non-empty cart; carries the amount due.</summary>
    public event EventHandler<decimal>? PaymentRequested;

    /// <summary>
    /// Raised for the cart line a scan just touched, so the view can scroll it into sight.
    /// Once the basket is more than a few items the newest line falls below the fold, and a
    /// cashier scanning fast has no confirmation the item landed.
    /// </summary>
    public event EventHandler<CartLine>? CartLineTouched;

    public SaleViewModel()
    {
        ProductsView = BuildProductsView();

        SubmitBarcodeCommand = new RelayCommand(_ => SubmitBarcode());
        AddProductCommand = new RelayCommand(p => { if (p is Product product) AddProduct(product); });
        IncrementCommand = new RelayCommand(l => { if (l is CartLine line) line.Quantity += line.Step; RaiseTotalsChanged(); });
        DecrementCommand = new RelayCommand(l => { if (l is CartLine line) Decrement(line); });
        RemoveLineCommand = new RelayCommand(l => { if (l is CartLine line) RemoveLine(line); });
        PayCommand = new RelayCommand(_ => RequestPayment(), _ => HasItems);
        HoldSaleCommand = new RelayCommand(_ => HoldSale(), _ => HasItems);
        ResumeTicketCommand = new RelayCommand(t => { if (t is HeldTicket ticket) ResumeTicket(ticket); });
        DiscardTicketCommand = new RelayCommand(t => { if (t is HeldTicket ticket) DiscardTicket(ticket); });
        ResumeLastTicketCommand = new RelayCommand(_ => ResumeLastTicket(), _ => HasHeldTickets);
        RemoveLastLineCommand = new RelayCommand(_ => RemoveLastLine(), _ => HasItems);
        ClearSearchCommand = new RelayCommand(_ =>
        {
            SearchText = string.Empty;
            StatusMessage = string.Empty;
            FocusBarcode();
        });

        Cart.CollectionChanged += (_, _) => RaiseTotalsChanged();
        HeldTickets.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasHeldTickets));
            ResumeLastTicketCommand.RaiseCanExecuteChanged();
        };
        ApplySort();
    }

    /// <summary>
    /// Rebinds the till to the catalogue. Catalog.Reload() replaces the product list with a
    /// new instance, so the old CollectionView would keep showing the products as they were
    /// before the back office edited them.
    /// </summary>
    public void ReloadProducts()
    {
        ProductsView = BuildProductsView();
        OnPropertyChanged(nameof(ProductsView));

        var selected = SelectedCategory;
        Categories.Clear();
        foreach (var category in Catalog.Categories) Categories.Add(category);
        SelectedCategory = Categories.Contains(selected) ? selected : "All";

        if (Page == PageKind.Products) LoadCategories();
        ApplySort();
        RefreshProducts();
    }

    /// <summary>
    /// The grid holds what a scanner cannot: bread, loose produce, anything with no barcode
    /// printed on it.
    ///
    /// A tile for a tin of tomatoes is a slower way of doing what the scanner does in half a
    /// second, and a hundred of them is a wall the cashier has to read past to find the one
    /// thing they actually need to press. So a product with a real barcode is scanned and
    /// nothing else; the grid is for everything else.
    ///
    /// Searching still reaches everything — typing part of a name finds a scanned product for
    /// the times a barcode is scuffed or the packet is torn.
    /// </summary>
    private ICollectionView BuildProductsView()
    {
        var view = CollectionViewSource.GetDefaultView(Catalog.Products);
        view.Filter = o => o is Product p
            && p.SoldAtTheTill
            && (SelectedCategory == "All" || p.Category == SelectedCategory)
            && (!p.IsScannable || IsSearching)
            && Matches(p, SearchText);
        return view;
    }

    /// <summary>
    /// True while the cashier is looking for something by name. Scanned products join the grid
    /// only then: a torn packet is exactly when somebody needs to find one by hand.
    /// </summary>
    private bool IsSearching => SearchText.Trim().Length > 0;

    private void RefreshProducts()
    {
        ProductsView.Refresh();
        OnPropertyChanged(nameof(HasVisibleProducts));
        OnPropertyChanged(nameof(IsShopEmpty));
        OnPropertyChanged(nameof(IsEverythingScannable));
        OnPropertyChanged(nameof(IsSaleGridVisible));
        OnPropertyChanged(nameof(IsSaleEmptyVisible));
    }

    private void ApplySort()
    {
        ProductsView.SortDescriptions.Clear();
        ProductsView.SortDescriptions.Add(SelectedSort switch
        {
            "Price: Low to High" => new SortDescription(nameof(Product.Price), ListSortDirection.Ascending),
            "Price: High to Low" => new SortDescription(nameof(Product.Price), ListSortDirection.Descending),
            _ => new SortDescription(nameof(Product.Name), ListSortDirection.Ascending),
        });
    }

    /// <summary>True when the product matches a free-text query (empty query matches everything).</summary>
    private static bool Matches(Product product, string? query)
    {
        query = query?.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        return product.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || product.Barcode.Contains(query, StringComparison.Ordinal);
    }

    private void SubmitBarcode()
    {
        var query = SearchText.Trim();
        FocusBarcode();

        if (query.Length == 0) return;

        // Checking a price never adds to the sale. This is before every other path on purpose:
        // the whole value of the mode is that the cashier can scan anything at all — a
        // customer's own shopping from another shop included — and nothing happens to the till.
        if (IsPriceCheck)
        {
            PriceCheckResult = PriceCheck.For(query);
            SearchText = string.Empty;
            StatusMessage = string.Empty;
            return;
        }

        // Scanner path first: an exact barcode always wins, so a scan never gets
        // reinterpreted as a name search.
        var scanned = Catalog.FindByBarcode(query);
        if (scanned is not null)
        {
            // Something the shop keeps a record of but does not sell. Saying so is kinder than
            // a silent nothing, which reads as a broken scanner.
            if (!scanned.SoldAtTheTill)
            {
                SetStatus(Loc.T("{0} is not sold at the till.", scanned.Name), isError: true);
                SearchText = string.Empty;
                return;
            }

            AddProduct(scanned);
            return;
        }

        // Typed path: narrow by name, and commit straight away if it's unambiguous.
        var matches = Catalog.Products.Where(p => Matches(p, query)).ToList();
        switch (matches.Count)
        {
            case 1:
                AddProduct(matches[0]);
                break;
            case 0:
                // A long run of digits is a scanner, so the miss means the shop does not sell
                // this yet — which is a different problem from a search with no results, and
                // one the cashier cannot fix from the till.
                SetStatus(LooksLikeABarcode(query)
                    ? Loc.T("{0} is not in the shop. Add it in the back office.", query)
                    : Loc.T("Nothing matches \"{0}\"", query), isError: true);
                break;
            default:
                // Leave the text in place so the grid stays filtered to the candidates.
                SetStatus(Loc.T("{0} matches — tap the one you want", matches.Count), isError: false);
                break;
        }
    }

    /// <summary>
    /// Long enough to be a scan rather than a search. Six digits is comfortably past any
    /// quantity or price a cashier would type into the box by hand.
    /// </summary>
    private static bool LooksLikeABarcode(string query) =>
        query.Length >= 6 && query.All(char.IsDigit);

    /// <summary>
    /// Puts a product the cashier has just created onto the sale, at the quantity or weight
    /// they entered. Separate from AddProduct, which steps an existing line by one — here the
    /// cashier has already said how much.
    /// </summary>
    public void AddCreatedProduct(Product product, decimal quantity)
    {
        var line = new CartLine(product, quantity);
        line.PropertyChanged += Line_PropertyChanged;
        Cart.Add(line);
        line.Flash();

        SearchText = string.Empty;
        StatusMessage = string.Empty;
        RaiseTotalsChanged();
        CartLineTouched?.Invoke(this, line);
        FocusBarcode();
    }

    private void AddProduct(Product product)
    {
        CartLine touched;
        var existing = Cart.FirstOrDefault(l => l.Product.Barcode == product.Barcode);
        if (existing is not null)
        {
            existing.Quantity += existing.Step;
            existing.Flash();
            touched = existing;
        }
        else
        {
            var line = new CartLine(product, product.Unit == Unit.Kg ? 1.0m : 1m);
            line.PropertyChanged += Line_PropertyChanged;
            Cart.Add(line);
            line.Flash();
            touched = line;
        }

        // Clearing the query resets the grid, ready for the next item.
        SearchText = string.Empty;
        StatusMessage = string.Empty;
        RaiseTotalsChanged();
        CartLineTouched?.Invoke(this, touched);
        FocusBarcode();
    }

    private void Decrement(CartLine line)
    {
        if (line.Quantity - line.Step <= 0)
        {
            RemoveLine(line);
            return;
        }
        line.Quantity -= line.Step;
        RaiseTotalsChanged();
    }

    private void RemoveLine(CartLine line)
    {
        line.PropertyChanged -= Line_PropertyChanged;
        Cart.Remove(line);
        RaiseTotalsChanged();
        FocusBarcode();
    }

    private void Line_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CartLine.LineTotal))
            RaiseTotalsChanged();
    }

    public void ClearCart()
    {
        foreach (var line in Cart)
            line.PropertyChanged -= Line_PropertyChanged;
        Cart.Clear();

        // A remise belongs to one customer, never to the next one.
        _discountKind = Models.DiscountKind.None;
        _discountValue = 0m;
        OnPropertyChanged(nameof(DiscountKind));
        OnPropertyChanged(nameof(DiscountValue));
        StatusMessage = string.Empty;
        RaiseTotalsChanged();
        FocusBarcode();
    }

    // ---------- Held tickets ----------
    //
    // Customer is halfway through, realises they forgot the eggs, and there's a queue.
    // Park their ticket, serve the next person on a fresh cart, then pick the parked one
    // back up. Any number of tickets can sit on hold at once.

    private void HoldSale()
    {
        if (!HasItems) return;
        ParkCurrentCart();
        FocusBarcode();
    }

    /// <summary>Moves the live cart into a new held ticket and leaves the cart empty.</summary>
    private HeldTicket ParkCurrentCart()
    {
        var lines = Cart.ToList();
        foreach (var line in lines)
            line.PropertyChanged -= Line_PropertyChanged;

        Cart.Clear();

        var ticket = new HeldTicket(NextTicketNumber(), lines);
        HeldTickets.Add(ticket);
        StatusMessage = string.Empty;
        RaiseTotalsChanged();
        return ticket;
    }

    /// <summary>
    /// Lowest number not currently on hold. A counter that only ever climbed left the
    /// cashier looking at "Ticket 8" with one ticket parked; numbers only need to be
    /// unique among the tickets actually on the strip right now.
    /// </summary>
    private int NextTicketNumber()
    {
        var inUse = HeldTickets.Select(t => t.Number).ToHashSet();
        var number = 1;
        while (inUse.Contains(number)) number++;
        return number;
    }

    private void ResumeTicket(HeldTicket ticket)
    {
        // Swapping straight from one customer to another: park what's on screen rather
        // than discarding it, or the cashier loses a sale with one mis-click.
        if (HasItems) ParkCurrentCart();

        HeldTickets.Remove(ticket);

        foreach (var line in ticket.Lines)
        {
            line.PropertyChanged += Line_PropertyChanged;
            Cart.Add(line);
        }

        StatusMessage = string.Empty;
        RaiseTotalsChanged();
        FocusBarcode();
    }

    /// <summary>F3 — pick the most recently parked ticket back up without reaching for the mouse.</summary>
    private void ResumeLastTicket()
    {
        if (HeldTickets.Count == 0) return;
        ResumeTicket(HeldTickets[^1]);
    }

    /// <summary>Ctrl+Z — undo the last scan, the mistake a cashier makes most often.</summary>
    private void RemoveLastLine()
    {
        if (Cart.Count == 0) return;
        RemoveLine(Cart[^1]);
    }

    private void DiscardTicket(HeldTicket ticket)
    {
        foreach (var line in ticket.Lines)
            line.PropertyChanged -= Line_PropertyChanged;

        HeldTickets.Remove(ticket);
        FocusBarcode();
    }

    private void RequestPayment()
    {
        if (!HasItems) return;
        PaymentRequested?.Invoke(this, Total);
    }

    /// <summary>
    /// Called by MainWindow once the payment dialog confirms. Writes the sale before
    /// clearing anything — if the insert throws, the cart stays put so the cashier can
    /// retry rather than losing a basket that has already been paid for.
    /// </summary>
    public void CompleteSale(PaymentMethod method, decimal amountTendered)
    {
        try
        {
            var invoiceNumber = SaleRepository.Save(
                Cart.Select(l => l.AsSaleItem).ToList(), GrossBeforeDiscount, DiscountKind, DiscountValue, DiscountAmount,
                Subtotal, Tax, Total, method, amountTendered);

            LastInvoiceNumber = invoiceNumber;
            ClearCart();
            if (IsTicketsPage) LoadTickets();

            // No success banner: the Payment Confirmed animation already says this, and a
            // second message left sitting on screen just gets stale. Failures still surface.
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("Could not save the sale: {0}", ex.Message), isError: true);
        }
    }

    /// <summary>
    /// How the customer paid. Cash, because that is the only way this shop takes money.
    ///
    /// The till used to offer Cash, Card and Other. Three buttons for a choice nobody makes
    /// is three chances to mis-record the takings — a card left selected from the last sale
    /// puts the next one in the wrong column, and nothing on any screen looks wrong.
    ///
    /// Kept as a property rather than removed outright: the column is still on every sale, so
    /// history and reports read back correctly, and a shop that starts taking cards later
    /// needs the buttons back rather than a migration.
    /// </summary>
    public PaymentMethod PaymentMethod => PaymentMethod.Cash;

    private int _lastInvoiceNumber;
    public int LastInvoiceNumber
    {
        get => _lastInvoiceNumber;
        private set => SetField(ref _lastInvoiceNumber, value);
    }

    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(Tax));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(GrossBeforeDiscount));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(DiscountLabel));
        OnPropertyChanged(nameof(HasDiscount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsCartPanelOpen));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(ItemCountLabel));
        PayCommand.RaiseCanExecuteChanged();
        HoldSaleCommand.RaiseCanExecuteChanged();
        RemoveLastLineCommand.RaiseCanExecuteChanged();
    }

    private void FocusBarcode() => RequestBarcodeFocus?.Invoke(this, EventArgs.Empty);
}
