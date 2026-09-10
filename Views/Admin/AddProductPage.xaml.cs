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

        // The ways an amount can be typed are the same on every visit, so the list is filled
        // once here rather than on each reset — and the form is never on screen without it.
        FillAddUnits(null);

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

        /// <summary>When it goes off, or a dash for the things that never do.</summary>
        public required string ExpiryLabel { get; init; }

        /// <summary>
        /// Gone off, or about to. Carried separately from the words so the row can be marked
        /// without the list having to parse its own text back.
        /// </summary>
        public required bool ExpiryNeedsAttention { get; init; }
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
            CostLabel = p.Cost > 0m ? Loc.Ltr($"{p.Cost:N2} DH") : "\u2014",
            PriceLabel = Loc.Ltr($"{p.Price:N2} DH"),
            StockLabel = p.Unit == Unit.Kg ? $"{p.Stock:0.###} kg" : $"{p.Stock:0.###}",
            AddedLabel = Ago(p),
            ExpiryLabel = p.ExpiryLabel,
            ExpiryNeedsAttention = p.ExpiryNeedsAttention,
        }).ToList();

        AddedEmpty.Visibility = products.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddListNote.Text = products.Count == 0
            ? Loc.T("Nothing in the shop yet")
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
            0 => Loc.T("today"),
            1 => Loc.T("yesterday"),
            < 7 => Loc.T("{0} days ago", days),
            < 14 => Loc.T("{0} week ago", 1),
            < 30 => Loc.T("{0} weeks ago", days / 7),
            _ => product.CreatedAt.ToString("d MMM yyyy"),
        };
    }

    /// <summary>Opens the scan prompt, then the form on whatever came back.</summary>
    private void AddNew_Click(object sender, RoutedEventArgs e)
    {
        var code = ScanWindow.Ask(Shell!);
        if (code is null) return;                       // backed out; stay on the list

        ShowAddForm();

        if (code.Length == 0)
        {
            // The scan prompt was answered with "this one has no barcode", which is the same
            // statement as the button on the form and has to land in the same place. It used
            // to mint the in-store code here and put it in the box — showing a twelve-digit
            // barcode to somebody who had just said there wasn't one.
            UseNoBarcode();
            return;
        }

        AddBarcodeBox.Text = code;

        if (_knownProduct is null)
        {
            AddIntro.Text = Loc.T("Not in the shop yet. Fill in the rest and save it.");
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

    /// <summary>
    /// Takes a product off the shelf from the list it was just added to.
    ///
    /// Hidden rather than deleted, exactly as on the stock list: every sale line ever recorded
    /// points at this row, so destroying it would take last year's receipts with it. It asks
    /// nothing first — a product typed in wrong is spotted a second after saving it, and a
    /// confirmation between the owner and their own mistake is a second tap for nothing. Show
    /// removed on the stock list brings any of them back.
    ///
    /// The click is stopped here: without that it would carry on up to the row itself, which
    /// opens the product for a delivery — the opposite of what was pressed.
    /// </summary>
    private void AddedRemove_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement { Tag: int id }) return;

        var product = StockRepository.Find(id);
        if (product is null) return;

        StockRepository.SetActive(product.Id, product.Name, active: false);
        Catalog.Reload();
        ShowAddList();
        AddListNote.Text = Loc.T("{0} removed from the shop", product.Name);
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
        _addUnits = AddUnitChoices;
        AddUnitBox.SelectedIndex = -1;
        FillAddUnits(null);

        AddCategoryBox.ItemsSource = CategoryRepository.List().Select(c => c.Name).ToList();
        AddCategoryBox.Text = (AddCategoryBox.ItemsSource as List<string>)?.FirstOrDefault() ?? string.Empty;

        ShowBarcodeRow(true);
        AddSaveButton.Content = Loc.T("Save product");
        AddFormTitle.Text = Loc.T("New product");
        AddIntro.Text = Loc.T("Scan the barcode, or leave it empty for goods with nothing printed on them.");

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
        var scanned = code.Length > 0;

        AddPictureNote.Text = Loc.T(scanned
            ? "The photo is optional here — this product is scanned, so it only shows on lists and receipts."
            : "Worth adding: with no barcode, this is what the cashier presses at the till.");
    }

    /// <summary>
    /// A way the shop can buy something, and what typing "3" into the amount box means if it
    /// is picked.
    ///
    /// <para>
    /// The database knows two units, per piece and per kilo, and that is the whole of what a
    /// price can mean. The rest of these are the same kilo counted differently — a delivery
    /// note that says 750 g — so it carries what to multiply the typed number by to get
    /// kilograms. The shop types what the supplier wrote down; the conversion happens here
    /// rather than in somebody's head.
    /// </para>
    /// </summary>
    private sealed record UnitChoice(
        string Name, Unit Unit, decimal InKilos, string AmountLabel, string Missing);

    private static readonly UnitChoice[] AddUnitChoices =
    {
        new("Per unit", Unit.Each, 1m,
            "QUANTITY", "Enter how many arrived."),
        new("Kilogram (kg)", Unit.Kg, 1m,
            "WEIGHT (KG)", "Enter the weight that arrived, in kilograms."),
        new("Gram (g)", Unit.Kg, 0.001m,
            "WEIGHT (G)", "Enter the weight that arrived, in grams."),
    };

    /// <summary>
    /// The choices currently in the list. Everything for a new product; only the ones that
    /// match when a known product is on screen, because a product already sold by the kilo
    /// cannot arrive by the piece and offering it is offering a mistake.
    /// </summary>
    private UnitChoice[] _addUnits = AddUnitChoices;

    private UnitChoice AddUnit =>
        _addUnits[Math.Clamp(AddUnitBox.SelectedIndex, 0, _addUnits.Length - 1)];

    private bool AddIsWeighed => AddUnit.Unit == Unit.Kg;

    /// <summary>
    /// Fills the list, keeping the way the shop is already typing if it still makes sense.
    /// </summary>
    private void FillAddUnits(Unit? soldAs)
    {
        var wanted = soldAs is { } only
            ? AddUnitChoices.Where(c => c.Unit == only).ToArray()
            : AddUnitChoices;

        var keep = AddUnitBox.SelectedIndex >= 0 && AddUnitBox.SelectedIndex < _addUnits.Length
            ? Array.IndexOf(wanted, _addUnits[AddUnitBox.SelectedIndex])
            : -1;

        _addUnits = wanted;
        AddUnitBox.ItemsSource = wanted.Select(c => Loc.T(c.Name)).ToList();
        AddUnitBox.SelectedIndex = Math.Max(0, keep);
    }

    private void AddUnit_Changed(object sender, RoutedEventArgs e) => UpdateAddUnitLabels();

    private void UpdateAddUnitLabels()
    {
        if (AddCostLabel is null || AddUnitBox.ItemsSource is null) return;

        // Money stays per piece or per kilo whichever way the amount is typed: a shop that
        // buys 300 g still knows what the kilo cost, and a price per gram would be three
        // decimal places of rounding error on every sale.
        AddCostLabel.Text = Loc.T(AddIsWeighed ? "BOUGHT FOR / KG" : "BOUGHT FOR");
        AddPriceLabel.Text = Loc.T(AddIsWeighed ? "SELLING FOR / KG" : "SELLING FOR");
        AddQuantityLabel.Text = Loc.T(AddUnit.AmountLabel);

        UpdateAddTotals();
    }

    /// <summary>What the shop typed into the amount box, in the unit the product is stored in.</summary>
    private bool TryAddQuantity(out decimal quantity)
    {
        var typed = TryAmount(AddQuantityBox.Text, out var amount) && amount > 0m;
        quantity = typed ? amount * AddUnit.InKilos : 0m;
        return typed;
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
        var hasQuantity = TryAddQuantity(out var quantity);

        AddTotalCost.Text = hasCost && hasQuantity && cost > 0m && quantity > 0m
            ? Loc.Ltr($"{Math.Round(cost * quantity, 2):N2} DH")
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

        if (code.Length == 0) { UseNoBarcode(); return; }

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
        FillAddUnits(found.Unit);
        AddCostBox.Text = found.Cost > 0m ? found.Cost.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
        AddPriceBox.Text = found.Price.ToString("0.00", CultureInfo.InvariantCulture);
        AddExpiryBox.SelectedDate = found.ExpiresOn;

        // The title carries the name and this line carries the state; a green badge saying the
        // same thing a third time was noise.
        AddFormTitle.Text = found.Name;
        AddIntro.Text = Loc.T("Already in the shop, {0} in stock. Enter how many arrived to add them.",
                              Loc.Ltr($"{found.Stock:0.###}"));
        AddSaveButton.Content = Loc.T("Add to stock");

        UpdateAddUnitLabels();
        FocusAdd(AddQuantityBox);
    }

    private void ClearKnownProduct()
    {
        _knownProduct = null;
        FillAddUnits(null);
        ShowBarcodeRow(true);
        AddSaveButton.Content = Loc.T("Save product");
        AddFormTitle.Text = Loc.T("New product");
        AddIntro.Text = Loc.T("Scan the barcode, or leave it empty for goods with nothing printed on them.");
    }

    /// <summary>
    /// Goods with nothing printed on them get an in-store code. The 2xxxxxxxxxxx range is
    /// reserved by EAN-13 for exactly this, so a shop-made code can never collide with a
    /// manufacturer's.
    /// </summary>
    /// <summary>
    /// The owner says this product has nothing printed on it.
    ///
    /// The barcode row goes away rather than filling with a number. It used to mint the
    /// in-store code here and put it in the box, which showed a twelve-digit code to somebody
    /// who had just pressed the button that says there isn't one — and invited them to edit it.
    ///
    /// Nothing is lost by hiding it: the code is minted at save, by the same line that has
    /// always handled an empty barcode. What the shop sees is what is true — this one has no
    /// barcode, and the shop will give it a code of its own.
    /// </summary>
    private void AddNoBarcode_Click(object sender, RoutedEventArgs e) => UseNoBarcode();

    /// <summary>
    /// Puts the form into "this one has no barcode" — reached from the button on the form and
    /// from the scan prompt, which are two ways of saying the same thing and must not behave
    /// differently.
    /// </summary>
    private void UseNoBarcode()
    {
        ClearKnownProduct();

        AddBarcodeBox.Clear();
        ShowBarcodeRow(false);
        AddIntro.Text = Loc.T("No barcode. Fill in the rest and save.");

        FocusAdd(AddNameBox);
    }

    /// <summary>Back to typing or scanning one, for a change of mind.</summary>
    private void AddHasBarcode_Click(object sender, RoutedEventArgs e)
    {
        ShowBarcodeRow(true);
        FocusAdd(AddBarcodeBox);
    }

    private void ShowBarcodeRow(bool showing)
    {
        AddBarcodeRow.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
        AddNoBarcodeNote.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
    }

    // ============================== Saving ==============================

    private void AddSave_Click(object sender, RoutedEventArgs e)
    {
        AddError.Text = string.Empty;

        if (!TryAddQuantity(out var quantity))
        {
            Fail(AddUnit.Missing, AddQuantityBox);
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

            // Empty stays empty: a product with nothing printed on it is saved with no
            // barcode at all, and that is what puts it on the till as something to press.
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
                Unit = AddUnit.Unit,
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
        AddError.Text = Loc.T(message);
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
