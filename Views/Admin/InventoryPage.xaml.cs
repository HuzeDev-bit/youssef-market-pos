using System.Windows;
using System.Windows.Controls;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// What the shop holds: the products, how many of each, what each one cost, and when it
/// goes off.
///
/// Three ways to narrow it, because a stocktake is never a walk down the whole shop. The
/// search finds one thing by name or barcode. The category filter takes an aisle at a time.
/// The date range answers the question the owner actually stands there asking — what goes
/// off between now and the end of the month — and the expiry column is what he reads off
/// once he has asked it.
///
/// Nothing here writes to stock directly. A row opens the count sheet, and that goes through
/// <see cref="InventoryRepository.Move"/>, so a shelf count and a breakage both leave a
/// movement behind and the chain stays unbroken.
/// </summary>
public partial class InventoryPage : AdminPageBase
{
    private List<StockItem> _rows = new();

    /// <summary>Set while the category list is being filled, so filling it is not a filter change.</summary>
    private bool _building;

    public InventoryPage() => InitializeComponent();

    public override string Title => "Inventory";
    public override string Subtitle => "What the shop holds, and what it cost";

    protected override void Load()
    {
        Session.Require(Permission.ManageInventory);

        FillCategories();

        _rows = InRange(StockRepository.List(search: SearchBox.Text, categoryId: SelectedCategoryId))
            .OrderBy(i => i.Name)
            .ToList();

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;

        ClearFilters.Visibility = IsFiltered ? Visibility.Visible : Visibility.Collapsed;

        ShowEmptyState();
        ShowSummary();
    }

    // ============================== The filters ==============================

    /// <summary>
    /// The aisles the shop actually made, kept in step with the Categories screen.
    ///
    /// Rebuilt on every load rather than once, because a category added on the next screen
    /// over should be here when the owner comes back — and the current choice is carried
    /// across by id, so refilling the list does not quietly reset the filter.
    /// </summary>
    private void FillCategories()
    {
        _building = true;

        var chosen = (CategoryFilter.SelectedItem as CategoryRow)?.Id ?? 0;

        var categories = new List<CategoryRow> { new() { Id = 0, Name = Loc.T("All categories") } };
        categories.AddRange(CategoryRepository.List());

        CategoryFilter.ItemsSource = categories;
        CategoryFilter.SelectedItem = categories.FirstOrDefault(c => c.Id == chosen) ?? categories[0];

        _building = false;
    }

    private int? SelectedCategoryId =>
        (CategoryFilter.SelectedItem as CategoryRow)?.Id is > 0 and var id ? id : null;

    private bool IsFiltered =>
        SearchBox.Text.Trim().Length > 0 || SelectedCategoryId is not null
        || FromDate.SelectedDate is not null || ToDate.SelectedDate is not null;

    /// <summary>
    /// Keeps what goes off inside the chosen window.
    ///
    /// Either end on its own is a half-open range, which is how the question is usually
    /// asked out loud: everything before the end of the month, or everything from today on.
    /// Stock with no expiry date drops out the moment a date is set — it has no date to be
    /// inside the range, and leaving it in would answer a different question than the one
    /// asked. With no dates set nothing is dropped, and the whole shop is on screen.
    /// </summary>
    private List<StockItem> InRange(List<StockItem> items)
    {
        var from = FromDate.SelectedDate?.Date;
        var to = ToDate.SelectedDate?.Date;

        if (from is null && to is null) return items;

        return items
            .Where(i => i.ExpiresOn is { } when
                     && (from is null || when.Date >= from)
                     && (to is null || when.Date <= to))
            .ToList();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !_building) Refresh();
    }

    /// <summary>
    /// The same thing, with the signature DatePicker insists on: its SelectedDateChanged is
    /// an EventHandler&lt;SelectionChangedEventArgs&gt;, not a routed event.
    /// </summary>
    private void Dates_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !_building) Refresh();
    }

    /// <summary>Back to the whole shop, in one press rather than four.</summary>
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _building = true;

        SearchBox.Clear();
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null;
        if (CategoryFilter.Items.Count > 0) CategoryFilter.SelectedIndex = 0;

        _building = false;
        Refresh();
    }

    // ============================== What it adds up to ==============================

    /// <summary>
    /// One line about what is on screen: how many products, what that stock cost, and how
    /// much of it needs dealing with this week.
    ///
    /// It describes the filtered list, not the shop, because the filtered list is what the
    /// reader is looking at. Stock with no cost recorded contributes nothing to the total,
    /// which makes the figure quietly too low — so it says how much is missing rather than
    /// letting the total be trusted whole.
    /// </summary>
    private void ShowSummary()
    {
        if (StockRepository.List().Count == 0)
        {
            // Blank read as a page that had failed to load. A shop with nothing in it is a
            // real state — every shop starts there — and it should say which one it is in.
            Summary.Text = Loc.T("Nothing in the shop yet");
            return;
        }

        var value = _rows.Sum(i => i.StockValue);
        var unpriced = _rows.Count(i => i.Cost <= 0m && i.Stock > 0m);
        var urgent = _rows.Count(i => i.ExpiryNeedsAttention);

        var parts = new List<string>
        {
            Loc.T(_rows.Count == 1 ? "{0} product · {1} of stock"
                                   : "{0} products · {1} of stock",
                  _rows.Count, Loc.Ltr($"{value:N2} {AppSettings.Current.Currency}")),
        };

        if (urgent > 0) parts.Add(Loc.T("{0} expiring or expired", urgent));

        if (unpriced > 0) parts.Add(Loc.T("{0} with no cost recorded", unpriced));

        Summary.Text = Loc.Join(parts);
    }

    private void ShowEmptyState()
    {
        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_rows.Count > 0) return;

        if (!IsFiltered)
        {
            EmptyTitle.Text = Loc.T("No products yet");
            EmptyBody.Text = Loc.T("Add what the shop sells under Add product, and it will appear here.");
            return;
        }

        // A filtered list that came back empty is not the same as an empty shop, and the
        // difference is the whole reason somebody would press Clear rather than worry.
        EmptyTitle.Text = Loc.T("Nothing matches");
        EmptyBody.Text = FromDate.SelectedDate is null && ToDate.SelectedDate is null
            ? Loc.T("Try a different name, barcode or category.")
            : Loc.T("Nothing in this category goes off between those dates. Stock with no expiry date is not counted.");
    }

    // ============================== Opening a row ==============================

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
