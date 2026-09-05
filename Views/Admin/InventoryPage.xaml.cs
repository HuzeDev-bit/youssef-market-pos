using System.Windows;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// What the shop holds: the products, how many of each, and what each one cost.
///
/// Deliberately just that. Categories, expiry dates and prices all have their own screens,
/// and a stock list that also tries to be those is one nobody can read down in a hurry with
/// a clipboard in the other hand.
///
/// Nothing here writes to stock directly. A row opens the count sheet, and that goes through
/// <see cref="InventoryRepository.Move"/>, so a shelf count and a breakage both leave a
/// movement behind and the chain stays unbroken.
/// </summary>
public partial class InventoryPage : AdminPageBase
{
    private List<StockItem> _rows = new();

    public InventoryPage() => InitializeComponent();

    public override string Title => "Inventory";
    public override string Subtitle => "What the shop holds, and what it cost";

    protected override void Load()
    {
        Session.Require(Permission.ManageInventory);

        _rows = StockRepository.List(search: SearchBox.Text)
            .OrderBy(i => i.Name)
            .ToList();

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;

        ShowEmptyState();
        ShowSummary();
    }

    /// <summary>
    /// One line: how many products, and what the stock cost the shop. Stock with no cost
    /// recorded contributes nothing to that total, which makes it quietly too low — so it
    /// says how much is missing rather than letting the figure be trusted whole.
    /// </summary>
    private void ShowSummary()
    {
        var all = StockRepository.List();
        var value = all.Sum(i => i.StockValue);
        var unpriced = all.Count(i => i.Cost <= 0m && i.Stock > 0m);

        if (all.Count == 0)
        {
            // Blank read as a page that had failed to load. A shop with nothing in it is a
            // real state — every shop starts there — and it should say which one it is in.
            Summary.Text = "Nothing in the shop yet";
            return;
        }

        var line = Loc.T(all.Count == 1 ? "{0} product · {1} of stock"
                                        : "{0} products · {1} of stock",
                         all.Count, Loc.Ltr($"{value:N2} {AppSettings.Current.Currency}"));

        Summary.Text = unpriced == 0
            ? line
            : $"{line} · {Loc.T("{0} with no cost recorded", unpriced)}";
    }

    private void ShowEmptyState()
    {
        var searching = SearchBox.Text.Trim().Length > 0;

        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_rows.Count > 0) return;

        EmptyTitle.Text = searching ? "Nothing matches" : "No products yet";
        EmptyBody.Text = searching
            ? "Try a different name or barcode."
            : "Add what the shop sells under Add product, and it will appear here.";
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    /// <summary>
    /// Opens the product itself: price, photo, barcode, supplier.
    ///
    /// Its own button rather than the row, because counting a shelf is the thing done daily
    /// and editing a product is the thing done once. Until this existed the product form was
    /// reachable only from a dashboard restock row, so a product that never ran low could
    /// never be corrected.
    /// </summary>
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || sender is not FrameworkElement { Tag: int id }) return;

        var item = StockRepository.Find(id);
        if (item is not null && ProductWindow.Edit(Shell, item)) ReloadAll();
    }

    /// <summary>The row opens the count sheet — the thing you do most often to a shelf.</summary>
    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || sender is not FrameworkElement { Tag: int id }) return;

        var item = _rows.FirstOrDefault(i => i.Id == id);
        if (item is not null && StockAdjustWindow.Show(Shell, item)) ReloadAll();
    }
}
