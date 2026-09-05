using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Add product: how goods get into the shop.
///
/// Built for repetition, because that is how stock arrives — a cashier works through a
/// delivery one box at a time. The barcode field takes focus on arrival and again after every
/// save, so the whole loop is scan, type, save, scan, with no mouse.
///
/// Scanning something the shop already sells is not an error: the form fills itself in from
/// what is on record and turns into "take delivery of more", which is what the cashier
/// actually meant.
/// </summary>
public partial class AddProductPage : AdminPageBase
{
    /// <summary>Set when the scanned barcode is already on the books — then this is a restock.</summary>
    private StockItem? _knownProduct;

    private readonly BarcodeScanner _scanner;

    /// <summary>A photo chosen for the product being added, not yet filed. Null means none.</summary>
    private string? _pickedPicture;

    public AddProductPage()
    {
        InitializeComponent();

        // Scanned digits have to reach the barcode field whatever has focus. The scanner
        // watches the whole page, and stands down while the list is showing or the caret is
        // already in the box the code belongs in.
        _scanner = new BarcodeScanner(this)
        {
            ShouldWatch = () => AddScroll.Visibility == Visibility.Visible
                             && !ReferenceEquals(Keyboard.FocusedElement, AddBarcodeBox),
        };
        _scanner.Scanned += (_, code) =>
        {
            AddBarcodeBox.Text = code;   // TextChanged looks it up and fills the form if known
            FocusAdd(_knownProduct is null ? AddNameBox : AddQuantityBox);
        };
    }

    public override string Title => "Add product";
    public override string Subtitle => "Put goods into the shop";

    protected override void Load() => ShowAddList();

    /// <summary>One row of the products list.</summary>
    public sealed class AddedRow
    {
        public required int Id { get; init; }
        public required string Name { get; init; }
        public required string Barcode { get; init; }
        public required string Category { get; init; }
        public required string CostLabel { get; init; }
        public required string PriceLabel { get; init; }
        public required string StockLabel { get; init; }
        public required string AddedLabel { get; init; }
    }

    // ============================== List and form ==============================

    /// <summary>The landing state: what is in the shop, newest first.</summary>
    private void ShowAddList()
    {
        AddListPanel.Visibility = Visibility.Visible;
        AddScroll.Visibility = Visibility.Collapsed;

        var products = StockRepository.RecentlyAdded();

        AddedList.ItemsSource = products.Select(p => new AddedRow
        {
            Id = p.Id,
            Name = p.Name,
            Barcode = p.Barcode,
            Category = p.Category,
            // A dash, never 0.00 — nothing is free to buy, and a zero here would be a figure
            // somebody might price against.
            CostLabel = p.Cost > 0m ? $"{p.Cost:N2} DH" : "\u2014",
            PriceLabel = $"{p.Price:N2} DH",
            StockLabel = p.Unit == Unit.Kg ? $"{p.Stock:0.###} kg" : $"{p.Stock:0.###}",
            AddedLabel = Ago(p),
        }).ToList();

        AddedEmpty.Visibility = products.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddListNote.Text = products.Count == 0
            ? "Nothing in the shop yet"
            : Loc.T(products.Count == 1 ? "{0} product, newest first"
                                        : "{0} products, newest first", products.Count);
    }

    /// <summary>Rough age, which is all this column is for — the exact minute helps nobody.</summary>
    private static string Ago(StockItem product)
    {
        // Read from the product's own created_at, never from the stock ledger: reading that
        // ledger needs SeeStockMovements, which a cashier does not have, and asking for it
        // here took the whole page down with "not allowed to SeeStockMovements".
        if (product.CreatedAt == DateTime.MinValue) return "\u2014";

        var days = (DateTime.Today - product.CreatedAt.Date).Days;
        return days switch
        {
            0 => "today",
            1 => "yesterday",
            < 7 => $"{days} days ago",
            < 30 => $"{days / 7} week{(days / 7 == 1 ? string.Empty : "s")} ago",
            _ => product.CreatedAt.ToString("d MMM yyyy"),
        };
    }

    /// <summary>Opens the scan prompt, then the form on whatever came back.</summary>
    private void AddNew_Click(object sender, RoutedEventArgs e)
    {
        var code = ScanWindow.Ask(Shell!);
        if (code is null) return;                       // backed out; stay on the list

        ShowAddForm();
        AddBarcodeBox.Text = code.Length > 0 ? code : StockRepository.NextInternalBarcode();

        if (_knownProduct is null)
        {
            AddIntro.Text = code.Length > 0
                ? "Not in the shop yet. Fill in the rest and save it."
                : "An in-store code has been made for it. Fill in the rest and save.";
            FocusAdd(AddNameBox);
        }
    }

    /// <summary>
    /// Pressing a row opens the same form on that product — its details, and a way to take
    /// delivery of more. Two screens for "look at it" and "add to it" would be one too many.
    /// </summary>
    private void AddedRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int id }) return;
        var product = StockRepository.Find(id);
        if (product is null) return;

        ShowAddForm();
        AddBarcodeBox.Text = product.Barcode;   // TextChanged fills the rest and flips to restock
        AddQuantityBox.Text = "0";
        FocusAdd(AddQuantityBox);
    }

    private void AddBack_Click(object sender, RoutedEventArgs e) => ShowAddList();

    /// <summary>
    /// Opens the blank form. Internal rather than private so the diagnostics can photograph
    /// it: flipping the two panels by hand skips the reset, and the picture that came back
    /// showed a form with none of its own explanatory text filled in.
    /// </summary>
    internal void ShowAddForm()
    {
        ResetAddForm();
        AddListPanel.Visibility = Visibility.Collapsed;
        AddScroll.Visibility = Visibility.Visible;
    }

    // ============================== The form ==============================

    private void ResetAddForm()
    {
        _knownProduct = null;

        _pickedPicture = null;

        AddBarcodeBox.Clear();
        AddNameBox.Clear();
        AddCostBox.Clear();
        AddPriceBox.Clear();
        AddQuantityBox.Text = "1";
        AddExpiryBox.SelectedDate = null;
        AddError.Text = string.Empty;
        AddPerUnit.IsChecked = true;

        AddCategoryBox.ItemsSource = CategoryRepository.List().Select(c => c.Name).ToList();
        AddCategoryBox.Text = (AddCategoryBox.ItemsSource as List<string>)?.FirstOrDefault() ?? string.Empty;

        AddSaveButton.Content = "Save product";
        AddFormTitle.Text = "New product";
        AddIntro.Text = "Scan the barcode, or leave it empty for goods with nothing printed on them.";

        UpdateAddUnitLabels();
        ShowAddPicture();
        FocusAdd(AddBarcodeBox);
    }

    // ============================== Photo ==============================

    private void AddPicture_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a photo for this product",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(Shell) != true) return;

        _pickedPicture = picker.FileName;
        ShowAddPicture();
    }

    /// <summary>
    /// Draws whichever photo applies: the one just chosen, or the one already on file for a
    /// product the barcode has been recognised as.
    /// </summary>
    private void ShowAddPicture()
    {
        if (AddPictureBox is null) return;

        var path = _pickedPicture
                   ?? (_knownProduct is null ? null : ProductImages.Find(_knownProduct.Barcode));
        var has = path is not null && System.IO.File.Exists(path);

        if (has)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path!);
            bitmap.DecodePixelWidth = 190;
            bitmap.EndInit();
            bitmap.Freeze();
            AddPictureBox.Source = bitmap;
        }
        else
        {
            AddPictureBox.Source = null;
        }

        AddPicturePrompt.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

        // What the photo is for depends on whether this thing will ever be a tile.
        var code = AddBarcodeBox.Text.Trim();
        var scanned = code.Length > 0 && !Product.IsShopsOwnCode(code);

        AddPictureNote.Text = scanned
            ? "The photo is optional here — this product is scanned, so it only shows on lists and receipts."
            : "Worth adding: with no barcode, this is what the cashier presses at the till.";
    }

    private bool AddIsWeighed => AddByWeight.IsChecked == true;

    private void AddUnit_Changed(object sender, RoutedEventArgs e) => UpdateAddUnitLabels();

    private void UpdateAddUnitLabels()
    {
        if (AddCostLabel is null) return;

        AddCostLabel.Text = AddIsWeighed ? "BOUGHT FOR / KG" : "BOUGHT FOR";
        AddPriceLabel.Text = AddIsWeighed ? "SELLING FOR / KG" : "SELLING FOR";
        AddQuantityLabel.Text = AddIsWeighed ? "WEIGHT (KG)" : "QUANTITY";

        UpdateAddTotals();
    }

    private void AddAmount_Changed(object sender, RoutedEventArgs e) => UpdateAddTotals();

    /// <summary>
    /// The two figures worth checking before saving: what this delivery cost, and what the
    /// shop makes each time one is sold. Both are visible while the prices are still being
    /// typed, which is when a wrong one can still be caught.
    /// </summary>
    private void UpdateAddTotals()
    {
        if (AddTotalCost is null) return;

        var hasCost = TryAmount(AddCostBox.Text, out var cost);
        var hasPrice = TryAmount(AddPriceBox.Text, out var price);
        var hasQuantity = TryAmount(AddQuantityBox.Text, out var quantity);

        AddTotalCost.Text = hasCost && hasQuantity && cost > 0m && quantity > 0m
            ? $"{Math.Round(cost * quantity, 2):N2} DH"
            : "—";

        if (!hasCost || !hasPrice || price <= 0m)
        {
            AddMargin.Text = "—";
            AddMargin.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Muted");
            return;
        }

        var margin = price - cost;
        AddMargin.Text = $"{margin:N2} DH  ·  {margin / price * 100m:0.#}%";

        // Selling below cost is the one thing here worth colouring: it loses money on every
        // single sale, quietly, until somebody notices.
        AddMargin.Foreground = (System.Windows.Media.Brush)FindResource(
            margin < 0m ? "Brush.Danger" : "Brush.Accent");
    }

    /// <summary>Accepts "8.50" and "8,50" — both keyboards turn up on a Moroccan counter.</summary>
    private static bool TryAmount(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
                         NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    // ============================== Barcode ==============================

    /// <summary>
    /// Clicking the barcode field asks for a scan rather than dropping a caret in an empty
    /// box. A scanner is just a keyboard — without a prompt there is nothing to say the
    /// machine is waiting, and nothing to say it worked.
    /// </summary>
    private void AddBarcode_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        var code = ScanWindow.Ask(Shell!);
        if (code is null) { FocusAdd(AddBarcodeBox); return; }

        AddBarcodeBox.Text = code;      // TextChanged looks it up and fills the form if known
        if (_knownProduct is null) FocusAdd(AddNameBox);
    }

    private void AddBarcode_KeyDown(object sender, KeyEventArgs e)
    {
        // A scanner ends with Enter. Move on to the name, which is the next thing to fill in.
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        FocusAdd(AddNameBox);
    }

    private void AddBarcode_Changed(object sender, RoutedEventArgs e)
    {
        LookUpBarcode();
        ShowAddPicture();
    }

    /// <summary>
    /// Recognises a barcode the shop already sells and switches the form to taking delivery of
    /// more of it. Scanning a product you already stock is the commonest thing that happens on
    /// this page; treating it as a duplicate-key error would be useless.
    /// </summary>
    private void LookUpBarcode()
    {
        if (AddIntro is null) return;

        var barcode = AddBarcodeBox.Text.Trim();
        var found = barcode.Length == 0
            ? null
            : StockRepository.List(includeInactive: true).FirstOrDefault(p => p.Barcode == barcode);

        if (found is null)
        {
            if (_knownProduct is not null) ClearKnownProduct();
            return;
        }

        if (_knownProduct?.Id == found.Id) return;

        _knownProduct = found;

        AddNameBox.Text = found.Name;
        AddCategoryBox.Text = found.Category;
        AddPerUnit.IsChecked = found.Unit == Unit.Each;
        AddByWeight.IsChecked = found.Unit == Unit.Kg;
        AddCostBox.Text = found.Cost > 0m ? found.Cost.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
        AddPriceBox.Text = found.Price.ToString("0.00", CultureInfo.InvariantCulture);
        AddExpiryBox.SelectedDate = found.ExpiresOn;

        // The title carries the name and this line carries the state; a green badge saying the
        // same thing a third time was noise.
        AddFormTitle.Text = found.Name;
        AddIntro.Text = $"Already in the shop, {found.Stock:0.###} in stock. "
                      + "Enter how many arrived to add them.";
        AddSaveButton.Content = "Add to stock";

        UpdateAddUnitLabels();
        FocusAdd(AddQuantityBox);
    }

    private void ClearKnownProduct()
    {
        _knownProduct = null;
        AddSaveButton.Content = "Save product";
        AddFormTitle.Text = "New product";
        AddIntro.Text = "Scan the barcode, or leave it empty for goods with nothing printed on them.";
    }

    /// <summary>
    /// Goods with nothing printed on them get an in-store code. The 2xxxxxxxxxxx range is
    /// reserved by EAN-13 for exactly this, so a shop-made code can never collide with a
    /// manufacturer's.
    /// </summary>
    private void AddNoBarcode_Click(object sender, RoutedEventArgs e)
    {
        ClearKnownProduct();
        AddBarcodeBox.Text = StockRepository.NextInternalBarcode();
        FocusAdd(AddNameBox);
    }

    // ============================== Saving ==============================

    private void AddSave_Click(object sender, RoutedEventArgs e)
    {
        AddError.Text = string.Empty;

        if (!TryAmount(AddQuantityBox.Text, out var quantity) || quantity <= 0m)
        {
            Fail(AddIsWeighed ? "Enter the weight that arrived, in kilograms." : "Enter how many arrived.",
                 AddQuantityBox);
            return;
        }

        TryAmount(AddCostBox.Text, out var cost);
        var hasPrice = TryAmount(AddPriceBox.Text, out var price);

        try
        {
            if (_knownProduct is { } known)
            {
                StockRepository.ReceiveAtTill(known.Id, quantity,
                    cost: cost > 0m ? cost : null,
                    price: hasPrice && price > 0m ? price : null,
                    expiresOn: AddExpiryBox.SelectedDate);

                // A delivery is also the moment somebody finally has the thing in their hand
                // to photograph it.
                if (_pickedPicture is not null) ProductImageWriter.Save(known.Barcode, _pickedPicture);

                Done($"{quantity:0.###} × {known.Name} added to stock");
                return;
            }

            var name = AddNameBox.Text.Trim();
            var category = AddCategoryBox.Text.Trim();
            var barcode = AddBarcodeBox.Text.Trim();

            if (name.Length == 0) { Fail("Give the product a name.", AddNameBox); return; }
            if (category.Length == 0) { Fail("Choose or type a category.", AddCategoryBox); return; }
            if (!hasPrice || price <= 0m) { Fail("Enter what it sells for.", AddPriceBox); return; }

            if (barcode.Length == 0) barcode = StockRepository.NextInternalBarcode();
            if (StockRepository.BarcodeTaken(barcode))
            {
                Fail("That barcode already belongs to another product.", AddBarcodeBox);
                return;
            }

            StockRepository.Create(new StockItem
            {
                Barcode = barcode,
                Name = name,
                Category = category,
                Cost = cost,
                Price = price,
                Unit = AddIsWeighed ? Unit.Kg : Unit.Each,
                TaxRate = VatForCategory(category),
                MinStock = AppSettings.Current.DefaultLowStock,
                ExpiresOn = AddExpiryBox.SelectedDate,
                ShowInPos = true,
            }, openingStock: quantity);

            // Filed after the save, under the barcode the product ended up with — which may be
            // an in-store code minted a line above this.
            if (_pickedPicture is not null) ProductImageWriter.Save(barcode, _pickedPicture);

            Done($"{name} saved · {quantity:0.###} in stock");
        }
        catch (Exception error)
        {
            AddError.Text = error.Message;
        }
    }

    /// <summary>Saved. Back to the list, where the row is now at the top.</summary>
    private void Done(string message)
    {
        Catalog.Reload();

        ShowAddList();
        AddListNote.Text = message;
    }

    /// <summary>
    /// Borrows the VAT bracket from whatever else is in that category, which is right far more
    /// often than any fixed default: bread and produce are zero-rated, drinks and cleaning are
    /// not. A brand new category falls back to the standard rate for the owner to correct.
    /// </summary>
    private static decimal VatForCategory(string category)
    {
        var siblings = Catalog.Products
            .Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return siblings.Count == 0
            ? 0.20m
            : siblings.GroupBy(p => p.TaxRate).OrderByDescending(g => g.Count()).First().Key;
    }

    private void Fail(string message, System.Windows.Controls.Control focus)
    {
        AddError.Text = message;
        FocusAdd(focus);
    }

    /// <summary>
    /// Focus is taken at Background priority: a field that has just been shown or enabled is
    /// not yet focusable, so asking during the click would silently do nothing.
    /// </summary>
    private void FocusAdd(System.Windows.Controls.Control control) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            control.Focus();
            Keyboard.Focus(control);
            if (control is System.Windows.Controls.TextBox box) box.SelectAll();
        }));
}
