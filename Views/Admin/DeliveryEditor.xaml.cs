using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// The list of goods on a delivery: what arrived, how many, what each cost, and what it will
/// sell for.
///
/// Its own control because two places ask the same question. Adding a supplier is almost
/// always somebody standing at the counter with a delivery, so that dialog takes the goods
/// too — and a second copy of this markup would drift from the first the day one of them
/// gained a rule.
/// </summary>
public partial class DeliveryEditor : UserControl
{
    private readonly List<PurchaseLine> _lines = new();

    /// <summary>The shelf price of the product being entered, to tell a change from a re-type.</summary>
    private decimal _wasSelling;

    private readonly BarcodeScanner _scanner;

    public DeliveryEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();

        // Goods come off the van with barcodes on them, so the obvious thing to do is scan
        // each one rather than type its name. Without this the digits landed in the product
        // box as text and — since a name that matches nothing is taken as a new product —
        // recording a delivery by scanner created products literally named "6111234500042".
        _scanner = new BarcodeScanner(this);
        _scanner.Scanned += (_, code) => TakeScan(code);
    }

    /// <summary>
    /// A scanned code. Selects the product it belongs to and moves to the quantity, which is
    /// the only thing left to say about it. An unknown code is put in the box as a barcode
    /// rather than a name, so saving creates the product against the code that was scanned.
    /// </summary>
    private void TakeScan(string code)
    {
        var known = Known.FirstOrDefault(p => p.Barcode == code);

        if (known is not null)
        {
            ProductBox.SelectedItem = known;
            ProductBox.Text = known.Name;
            Product_Changed(this, new RoutedEventArgs());
            QuantityBox.Focus();
            QuantityBox.SelectAll();
            return;
        }

        _scannedCode = code;
        ProductBox.SelectedItem = null;
        ProductBox.Text = string.Empty;
        ProductBox.Focus();
        ShowMargin();
    }

    /// <summary>
    /// The barcode of something scanned that the shop does not stock yet. Held so the name the
    /// owner types next is saved against the code on the box, rather than the product being
    /// given a fresh in-store code it does not need.
    /// </summary>
    private string? _scannedCode;

    /// <summary>Raised whenever the lines change, so the host can restate its total.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<PurchaseLine> Lines => _lines;

    public decimal Total => _lines.Sum(l => l.LineTotal);

    /// <summary>Lines whose new price is under what was paid for them — worth asking about.</summary>
    public IReadOnlyList<PurchaseLine> BelowCost =>
        _lines.Where(l => l.SellPrice is { } price && price > 0m && price < l.UnitCost).ToList();

    /// <summary>What the shop already sells, for matching a typed name against.</summary>
    private List<StockItem> Known { get; set; } = new();

    /// <summary>Re-reads the catalogue, so a product added elsewhere turns up without a restart.</summary>
    public void Reload()
    {
        Known = StockRepository.List();
        ProductBox.ItemsSource = Known;
    }

    public void FocusProduct() => ProductBox.Focus();

    /// <summary>True when Enter should mean "add this line" — the caller owns the key handling.</summary>
    public bool WantsEnter => CostBox.IsKeyboardFocusWithin || SellBox.IsKeyboardFocusWithin;

    /// <summary>Keys for products typed in but not yet created. Negative, so they never collide.</summary>
    private int _nextNewKey = -1;

    /// <summary>
    /// Adds whatever is in the boxes. Returns the complaint, or null when it worked.
    ///
    /// The product does not have to exist. A delivery is where a shop meets a new line for
    /// the first time - the van brings something it has never sold - and refusing the name
    /// until somebody goes and creates the product elsewhere is a dead end with the invoice
    /// still in hand. A typed name becomes a real product when the delivery is saved.
    /// </summary>
    public string? AddLine()
    {
        var typed = (ProductBox.Text ?? string.Empty).Trim();

        // By barcode first: a scan that reached this box as text is a code, not a name, and
        // matching it against names would make a new product out of it.
        var product = ProductBox.SelectedItem as StockItem
            ?? Known.FirstOrDefault(p => p.Barcode == typed)
            ?? Known.FirstOrDefault(p => string.Equals(p.Name, typed,
                                                       StringComparison.CurrentCultureIgnoreCase));

        if (product is null && typed.Length == 0)
        {
            ProductBox.Focus();
            return _scannedCode is null
                ? "Name what arrived, or pick it from the list."
                : $"{_scannedCode} is not in the shop yet. Give it a name.";
        }

        // A run of digits is a barcode somebody scanned or typed, and no shop calls a product
        // by its number. Naming one that way is always a mistake, and a silent one.
        if (product is null && typed.Length >= BarcodeScanner.MinimumLength && typed.All(char.IsAsciiDigit))
        {
            _scannedCode = typed;
            ProductBox.Text = string.Empty;
            ProductBox.Focus();
            return $"{typed} is not in the shop yet. Give it a name, not its number.";
        }
        if (!TryMoney(QuantityBox.Text, out var quantity) || quantity <= 0m)
        {
            QuantityBox.Focus();
            return "Enter how many arrived.";
        }
        if (!TryMoney(CostBox.Text, out var cost) || cost < 0m)
        {
            CostBox.Focus();
            return "Enter what each one cost.";
        }

        // A sell price is only carried when one was actually typed. Left blank, the shelf
        // price stays where it is rather than being reset to zero by an empty box.
        decimal? sellPrice = TryMoney(SellBox.Text, out var sell) && sell > 0m ? sell : null;

        // A product that has never been priced is the exception: there is no old price to
        // leave alone, so one has to be given or the till would sell it at cost.
        if (product is null && sellPrice is null)
        {
            SellBox.Focus();
            return $"Enter what {typed} sells for - it is new to the shop.";
        }

        var name = product?.Name ?? typed;

        // The same product twice on one invoice is a mistake far more often than it is real,
        // so the quantities are merged rather than creating a second line. New ones match on
        // the name, since they have no id to match on yet.
        var existing = product is not null
            ? _lines.FirstOrDefault(l => l.ProductId == product.Id)
            : _lines.FirstOrDefault(l => l.IsNew &&
                  string.Equals(l.Name, name, StringComparison.CurrentCultureIgnoreCase));

        if (existing is not null)
        {
            _lines.Remove(existing);
            quantity += existing.Quantity;
        }

        _lines.Add(new PurchaseLine
        {
            ProductId = product?.Id ?? _nextNewKey--,
            Name = name,
            Quantity = quantity,
            UnitCost = cost,
            SellPrice = sellPrice,
            // Only for a new product, and only when one was actually scanned: an existing
            // product keeps the code it already has.
            Barcode = product is null ? _scannedCode : null,
        });

        QuantityBox.Clear();
        CostBox.Clear();
        CostBox.Tag = null;
        SellBox.Clear();
        SellBox.Tag = null;
        _wasSelling = 0m;
        _scannedCode = null;
        ProductBox.SelectedItem = null;
        ProductBox.Text = string.Empty;
        ProductBox.Focus();

        Refresh();
        return null;
    }

    private void AddLine_Click(object sender, RoutedEventArgs e) => Raise(AddLine());

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int productId }) return;
        _lines.RemoveAll(l => l.ProductId == productId);
        Refresh();
    }

    /// <summary>Surfaces a complaint the caller did not ask for, since the click came from here.</summary>
    private void Raise(string? problem)
    {
        if (problem is not null) Problem?.Invoke(this, problem);
    }

    /// <summary>Raised when a line could not be added, carrying what to tell the owner.</summary>
    public event EventHandler<string>? Problem;

    private void Refresh()
    {
        LineList.ItemsSource = null;
        LineList.ItemsSource = _lines;
        LinesEmpty.Visibility = _lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowMargin();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ============================== Prices ==============================

    /// <summary>
    /// Pre-fills both prices with what this product costs and sells for now, which is usually
    /// right. Typing over either one clears the auto flag, so a figure the owner entered is
    /// never quietly replaced when they change their mind about the product.
    /// </summary>
    private void Product_Changed(object sender, RoutedEventArgs e)
    {
        if (ProductBox.SelectedItem is not StockItem product) return;

        if (CostBox.Text.Trim().Length == 0 || CostBox.Tag as string == "auto")
        {
            CostBox.Text = product.Cost.ToString("0.00", CultureInfo.InvariantCulture);
            CostBox.Tag = "auto";
        }

        if (SellBox.Text.Trim().Length == 0 || SellBox.Tag as string == "auto")
        {
            SellBox.Text = product.Price.ToString("0.00", CultureInfo.InvariantCulture);
            SellBox.Tag = "auto";
        }

        _wasSelling = product.Price;
        ShowMargin();
    }

    private void Prices_Changed(object sender, RoutedEventArgs e) => ShowMargin();

    /// <summary>Typing a name changes whether it is a new product, so the note has to keep up.</summary>
    private void Product_Typed(object sender, TextChangedEventArgs e) => ShowMargin();

    /// <summary>
    /// What the shop makes on each one, said while the prices are still being typed. Selling
    /// below cost is the one thing worth colouring: it loses money on every sale, quietly,
    /// until somebody notices.
    /// </summary>
    private void ShowMargin()
    {
        if (MarginText is null) return;

        var hasCost = TryMoney(CostBox.Text, out var cost) && cost > 0m;
        var hasSell = TryMoney(SellBox.Text, out var sell) && sell > 0m;

        var typed = (ProductBox.Text ?? string.Empty).Trim();
        var isNew = typed.Length > 0 && ProductBox.SelectedItem is null &&
                    !Known.Any(p => string.Equals(p.Name, typed, StringComparison.CurrentCultureIgnoreCase));

        var newNote = _scannedCode is not null && typed.Length == 0
            ? $"Scanned {_scannedCode} - not in the shop yet. Give it a name.  ·  "
            : isNew ? $"{typed} is new - it will be added to the shop.  ·  "
            : string.Empty;

        if (!hasCost || !hasSell)
        {
            MarginText.Text = newNote;
            MarginText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Muted");
            return;
        }

        var margin = sell - cost;
        var moved = ProductBox.SelectedItem is StockItem && sell != _wasSelling
            ? $"  ·  price changes from {_wasSelling:N2} to {sell:N2}"
            : string.Empty;

        MarginText.Text = newNote + (margin <= 0m
            ? $"Selling at {sell:N2} loses {-margin:N2} on every one.{moved}"
            : $"Makes {margin:N2} each, {margin / sell * 100m:0}% of the price.{moved}");

        MarginText.Foreground = (System.Windows.Media.Brush)FindResource(
            margin <= 0m ? "Brush.Danger" : "Brush.Muted");
    }

    internal static bool TryMoney(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
                         NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
