using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Add or edit a product.
///
/// The one rule the form enforces beyond "fill in the box": on an existing product, stock is
/// read-only. Correcting a count belongs on the Inventory page, which asks why it changed and
/// writes a movement. Letting it be typed over here would put a hole in the stock history
/// that nothing could explain afterwards.
/// </summary>
public partial class ProductWindow : Window
{
    private readonly StockItem? _existing;
    private readonly List<Supplier> _suppliers;

    /// <summary>A photo chosen in this dialog but not yet saved. Null means "leave it alone".</summary>
    private string? _pickedFrom;

    /// <summary>True when the owner pressed Remove photo. Distinct from having picked nothing.</summary>
    private bool _dropPicture;

    private static readonly (string Label, decimal Rate)[] TaxRates =
    [
        ("0% — basic food", 0m),
        ("7%", 0.07m),
        ("10%", 0.10m),
        ("20% — standard", 0.20m),
    ];

    public ProductWindow(StockItem? existing)
    {
        InitializeComponent();
        _existing = existing;

        UnitBox.ItemsSource = new[] { "Each / piece", "Kilogram" };
        TaxBox.ItemsSource = TaxRates.Select(t => t.Label).ToList();
        CategoryBox.ItemsSource = CategoryRepository.List().Select(c => c.Name).ToList();

        _suppliers = new List<Supplier> { new() { Id = 0, Name = "No supplier" } };
        _suppliers.AddRange(SupplierRepository.List());
        SupplierBox.ItemsSource = _suppliers;

        if (existing is null) FillForNew(); else FillFrom(existing);

        // Only a VAT-registered shop has a bracket to choose. The rest of the form does not
        // move: the column stays where it is, empty, so the fields either side keep their
        // places between one shop and another.
        TaxField.Visibility = AppSettings.Current.TaxId.Trim().Length > 0
            ? Visibility.Visible
            : Visibility.Hidden;

        ShowPicture();
        Loaded += (_, _) => NameBox.Focus();
    }

    public static bool AddNew(Window owner) =>
        new ProductWindow(null) { Owner = owner }.ShowDialog() == true;

    public static bool Edit(Window owner, StockItem item) =>
        new ProductWindow(item) { Owner = owner }.ShowDialog() == true;

    private void FillForNew()
    {
        HeadingText.Text = "Add product";
        SubText.Text = "It goes on the till as soon as you save.";
        Title = "Add product";

        UnitBox.SelectedIndex = 0;
        TaxBox.SelectedIndex = 3;
        SupplierBox.SelectedIndex = 0;
        StockBox.Text = "0";
        MinStockBox.Text = AppSettings.Current.DefaultLowStock.ToString("0.###", CultureInfo.InvariantCulture);
        ShowInPosBox.IsChecked = true;
        CategoryBox.Text = (CategoryBox.ItemsSource as List<string>)?.FirstOrDefault() ?? "Grocery";
        UpdateMargin();
    }

    private void FillFrom(StockItem item)
    {
        HeadingText.Text = item.Name;
        SubText.Text = $"{item.Barcode} · {item.Category}";
        Title = item.Name;

        NameBox.Text = item.Name;
        CategoryBox.Text = item.Category;
        BarcodeBox.Text = item.Barcode;
        SkuBox.Text = item.Sku;
        UnitBox.SelectedIndex = item.Unit == Unit.Kg ? 1 : 0;
        CostBox.Text = item.Cost.ToString("0.00", CultureInfo.InvariantCulture);
        PriceBox.Text = item.Price.ToString("0.00", CultureInfo.InvariantCulture);

        var taxIndex = Array.FindIndex(TaxRates, t => t.Rate == item.TaxRate);
        TaxBox.SelectedIndex = taxIndex >= 0 ? taxIndex : 3;

        StockBox.Text = item.Stock.ToString("0.###", CultureInfo.InvariantCulture);
        StockBox.IsReadOnly = true;
        StockBox.Opacity = 0.6;
        StockLabel.Text = "STOCK (CHANGE IT ON INVENTORY)";
        StockBox.ToolTip = "Stock is changed on the Inventory page, so every movement has a reason recorded.";

        MinStockBox.Text = item.MinStock.ToString("0.###", CultureInfo.InvariantCulture);
        ShelfBox.Text = item.Shelf;
        ExpiryBox.SelectedDate = item.ExpiresOn;
        SupplierBox.SelectedItem = _suppliers.FirstOrDefault(s => s.Id == (item.SupplierId ?? 0)) ?? _suppliers[0];
        ShowInPosBox.IsChecked = item.ShowInPos;

        UpdateMargin();
    }

    // ------------------------------- Validation -------------------------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var name = NameBox.Text.Trim();
        var category = CategoryBox.Text.Trim();
        var barcode = BarcodeBox.Text.Trim();

        if (name.Length == 0) { Fail("Give the product a name.", NameBox); return; }
        if (category.Length == 0) { Fail("Choose or type a category.", CategoryBox); return; }
        if (barcode.Length == 0) { Fail("Enter a barcode, or press Generate for an in-store code.", BarcodeBox); return; }

        if (!TryMoney(PriceBox.Text, out var price) || price < 0m)
        {
            Fail("The selling price must be a number, like 8.50.", PriceBox);
            return;
        }
        if (!TryMoney(CostBox.Text, out var cost) || cost < 0m)
        {
            Fail("The purchase price must be a number, like 6.20.", CostBox);
            return;
        }
        if (!TryMoney(MinStockBox.Text, out var minStock) || minStock < 0m)
        {
            Fail("The minimum stock must be a number.", MinStockBox);
            return;
        }
        if (!TryMoney(StockBox.Text, out var stock) || stock < 0m)
        {
            Fail("The stock quantity must be a number.", StockBox);
            return;
        }

        if (StockRepository.BarcodeTaken(barcode, _existing?.Id ?? 0))
        {
            Fail("Another product already uses that barcode.", BarcodeBox);
            return;
        }

        var supplierId = (SupplierBox.SelectedItem as Supplier)?.Id;
        var item = new StockItem
        {
            Id = _existing?.Id ?? 0,
            Name = name,
            Category = category,
            Barcode = barcode,
            Sku = SkuBox.Text.Trim(),
            Cost = cost,
            Price = price,
            MinStock = minStock,
            Unit = UnitBox.SelectedIndex == 1 ? Unit.Kg : Unit.Each,
            TaxRate = TaxRates[Math.Max(0, TaxBox.SelectedIndex)].Rate,
            Shelf = ShelfBox.Text.Trim(),
            SupplierId = supplierId is > 0 ? supplierId : null,
            ExpiresOn = ExpiryBox.SelectedDate,
            ShowInPos = ShowInPosBox.IsChecked == true,

            // Only a path somebody set deliberately is stored. The catalogue finds the usual
            // file by barcode on its own, and writing that discovered path back would bake
            // this machine's own folder into a row a second till has to read.
            ImagePath = ProductImages.IsTheUsualPlace(_existing?.ImagePath, barcode)
                ? null
                : _existing?.ImagePath,
        };

        try
        {
            if (_existing is null)
                StockRepository.Create(item, openingStock: stock);
            else
                StockRepository.Update(item);

            FilePicture(barcode, _existing?.Barcode);
            Catalog.Reload();
            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    // ------------------------------- Photo -------------------------------

    private void Picture_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a photo for this product",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(this) != true) return;

        _pickedFrom = picker.FileName;
        _dropPicture = false;
        ShowPicture();
    }

    private void RemovePicture_Click(object sender, RoutedEventArgs e)
    {
        _pickedFrom = null;
        _dropPicture = true;
        ShowPicture();
    }

    /// <summary>
    /// Draws whichever photo is current: the one just chosen, or the one already on file.
    /// Loaded with OnLoad so the file is not left open — the same photo has to be replaceable
    /// without closing the back office.
    /// </summary>
    private void ShowPicture()
    {
        var path = _dropPicture
            ? null
            : _pickedFrom ?? (_existing is null ? null : ProductImages.Find(_existing.Barcode));

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
            PictureBox.Source = bitmap;
        }
        else
        {
            PictureBox.Source = null;
        }

        PicturePrompt.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        RemovePicture.Visibility = has ? Visibility.Visible : Visibility.Collapsed;

        // Says what the photo is actually for, which depends on whether this product will
        // ever appear as something to press.
        var scanned = BarcodeBox.Text.Trim() is { Length: > 0 } code && !Product.IsShopsOwnCode(code);
        PictureNote.Text = scanned
            ? "Optional. This product is scanned, so the photo only shows on receipts and lists."
            : "Shown on the till, where the cashier presses it. Worth adding for anything without a barcode.";
    }

    /// <summary>
    /// Files the photo under the barcode the product is being saved with. Done after the save
    /// because a new product has no barcode until Generate has run, and because a rename of
    /// the code has to take its picture with it.
    /// </summary>
    private void FilePicture(string barcode, string? previousBarcode)
    {
        if (previousBarcode is { Length: > 0 } old && old != barcode)
        {
            // The code changed. Move the photo rather than orphaning it.
            var existingPhoto = ProductImages.Find(old);
            if (existingPhoto is not null && _pickedFrom is null && !_dropPicture)
                _pickedFrom = existingPhoto;
            ProductImages.Forget(old);
        }

        if (_dropPicture) { ProductImages.Forget(barcode); return; }
        if (_pickedFrom is null) return;

        ProductImageWriter.Save(barcode, _pickedFrom);
    }

    private void Fail(string message, System.Windows.Controls.Control focus)
    {
        ErrorText.Text = message;
        focus.Focus();
    }

    /// <summary>
    /// Accepts both "8.50" and "8,50" — a Moroccan keyboard and a French Windows will both
    /// happen on this counter, and rejecting one of them is a support call every week.
    /// </summary>
    private static bool TryMoney(string text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
                         NumberStyles.Number, CultureInfo.InvariantCulture, out value)
        || (string.IsNullOrWhiteSpace(text) && (value = 0m) == 0m);

    // ------------------------------- Live margin -------------------------------

    private void Money_Changed(object sender, RoutedEventArgs e) => UpdateMargin();

    /// <summary>
    /// Shows the margin as the prices are typed. An owner pricing a shelf wants to know what
    /// they are making before they save, not after the month's report.
    /// </summary>
    private void UpdateMargin()
    {
        if (MarginText is null) return;

        TryMoney(CostBox.Text, out var cost);
        TryMoney(PriceBox.Text, out var price);

        if (price <= 0m)
        {
            MarginText.Text = "—";
            MarginWarning.Visibility = Visibility.Collapsed;
            return;
        }

        var margin = price - cost;
        var percent = margin / price * 100m;
        MarginText.Text = $"{margin:0.00} DH · {percent:0.#}%";

        MarginText.Foreground = (System.Windows.Media.Brush)FindResource(
            margin < 0m ? "Brush.Danger" : "Brush.Text");
        MarginWarning.Visibility = margin < 0m ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GenerateBarcode_Click(object sender, MouseButtonEventArgs e)
    {
        BarcodeBox.Text = StockRepository.NextInternalBarcode();

        // An in-store code means this product will be a tile at the till, so what the photo is
        // for has just changed. Say so rather than leaving the old note under it.
        ShowPicture();
    }

    private void Barcode_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsLoaded) ShowPicture();
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
