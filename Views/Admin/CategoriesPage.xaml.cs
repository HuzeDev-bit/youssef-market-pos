using System.Windows;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// How products are grouped on the till.
///
/// Categories exist for one reason: a cashier holding something with no barcode — loose
/// tomatoes, a loaf off the rack — has to be able to find it. So the number that matters on
/// this page is not how many categories there are, it is how many products belong to none of
/// them, because each of those is a thing the till can only reach by typing its name.
///
/// Shown as cards rather than rows because that is how the till shows them, and because a
/// category is an icon and a name, which a table column would only make smaller.
/// </summary>
public partial class CategoriesPage : AdminPageBase
{
    private List<CategoryRow> _rows = new();

    public CategoriesPage() => InitializeComponent();

    public override string Title => "Categories";
    public override string Subtitle => "How products are grouped on the till";

    protected override void Load()
    {
        Session.Require(Permission.ManageCategories);

        var products = StockRepository.List();
        _rows = CategoryRepository.List(includeInactive: ShowInactive.IsChecked == true);

        // Each card carries what is actually in it. Read from the products rather than kept
        // on the category, so it cannot drift out of step with the shelves.
        foreach (var row in _rows)
        {
            var inside = products.Where(p => p.CategoryId == row.Id).ToList();
            row.StockValue = inside.Sum(p => p.StockValue);
            row.RetailValue = inside.Sum(p => p.RetailValue);
            row.LowCount = inside.Count(p => p.Status != StockStatus.InStock);
        }

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;
        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        FillSummary(products);
    }

    private void FillSummary(List<StockItem> products)
    {
        // Counted from the products themselves rather than from the category rows: a product
        // pointing at a category that has since been hidden is still grouped, and a product
        // pointing at nothing is the one the till cannot find.
        var loose = products.Count(p => p.CategoryId <= 0);
        var grouped = products.Count - loose;

        var active = _rows.Where(c => c.IsActive).ToList();
        var hidden = _rows.Count - active.Count;
        var withPictures = active.Count(c => c.HasPicture);
        var value = _rows.Sum(c => c.StockValue);

        CountValue.Text = active.Count.ToString();
        CountNote.Text = hidden > 0
            ? Loc.T("{0} hidden from the till", hidden)
            : active.Count == 0 ? Loc.T("none yet")
            : withPictures == active.Count ? Loc.T("all with pictures")
            : Loc.T("{0} with a picture", withPictures);

        GroupedValue.Text = grouped.ToString();
        GroupedNote.Text = products.Count == 0
            ? Loc.T("no products yet")
            : Loc.T(products.Count == 1 ? "of {0} product" : "of {0} products", products.Count);

        LooseValue.Text = loose.ToString();
        LooseNote.Text = Loc.T(loose == 0
            ? "everything is grouped"
            : "only findable by barcode or name");

        ValueValue.Text = Loc.Ltr($"{value:N2} {AppSettings.Current.Currency}");
        ValueNote.Text = Loc.T(value <= 0m
            ? "no cost recorded yet"
            : "what the stock in them cost");

        Hint.Text = loose == 0
            ? string.Empty
            : Loc.T(loose == 1
                ? "{0} product has no category. A cashier can only reach it by scanning or typing the name."
                : "{0} products have no category. A cashier can only reach them by scanning or typing the name.",
                loose);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is not null && CategoryWindow.AddNew(Shell)) ReloadAll();
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (Shell is null || sender is not FrameworkElement { Tag: int id }) return;

        var row = _rows.FirstOrDefault(c => c.Id == id);
        if (row is not null && CategoryWindow.Edit(Shell, row)) ReloadAll();
    }
}
