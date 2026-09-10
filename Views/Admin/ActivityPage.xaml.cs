using System.Windows;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Who changed what, and every movement of stock.
///
/// Two records that answer the same question from different sides. The activity log says a
/// person did a thing; the stock ledger says a quantity moved. Apart, "who emptied that
/// shelf" needs both screens open — so they are one list here, in the order it happened.
///
/// Nothing on this page can be edited. That is the point of it: a record somebody can tidy
/// up is not a record.
/// </summary>
public partial class ActivityPage : AdminPageBase
{
    /// <summary>One thing that happened, from either record.</summary>
    private sealed class Entry
    {
        public required DateTime At { get; init; }
        public required string Initial { get; init; }
        public required string Sentence { get; init; }
        public required string Kind { get; init; }

        /// <summary>Which list it came from, for the filter.</summary>
        public required bool IsStock { get; init; }

        public string When => At.Date == DateTime.Today
            ? At.ToString("HH:mm")
            : At.ToString("d MMM HH:mm");
    }

    /// <summary>
    /// Translated where it is built, not in the XAML: this is a bound list, and bound text is
    /// the shop's own data as far as the translator is concerned. These three words are the
    /// app's.
    /// </summary>
    private static string[] Kinds =>
        [Loc.T("Everything"), Loc.T("People"), Loc.T("Stock movements")];

    private List<Entry> _rows = new();
    private bool _building;

    public ActivityPage() => InitializeComponent();

    public override string Title => "Activity log";
    public override string Subtitle => "Who changed what, and every movement of stock";
    public override bool UsesDateRange => true;

    protected override void Load()
    {
        Session.Require(Permission.SeeActivityLog);

        FillKindFilter();

        var search = SearchBox.Text.Trim();
        var rows = new List<Entry>();

        if (KindFilter.SelectedIndex != 2)
        {
            rows.AddRange(Link.Shop.Activity.List(Dates.Range, search).Select(a => new Entry
            {
                At = a.HappenedAt,
                Initial = a.Initial,
                Sentence = a.Sentence,
                Kind = Loc.T(a.Entity.Length > 0 ? a.Entity : "Shop"),
                IsStock = false,
            }));
        }

        if (KindFilter.SelectedIndex != 1 && Session.Can(Permission.SeeStockMovements))
        {
            rows.AddRange(Movements(search));
        }

        _rows = rows.OrderByDescending(e => e.At).ToList();

        Rows.ItemsSource = null;
        Rows.ItemsSource = _rows;

        ShowEmptyState(search);
        ShowSummary();
    }

    /// <summary>
    /// Stock movements as sentences. The ledger stores what moved and by how much; a person
    /// reading a log wants "Ahmed took 3 off Milk 1L", not a signed number in a column.
    /// </summary>
    private IEnumerable<Entry> Movements(string search)
    {
        var moves = Link.Shop.Stock.Movements(Dates.Range);

        if (search.Length > 0)
        {
            moves = moves
                .Where(m => m.ProductName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                         || m.WorkerName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                         || m.ReasonLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                         || m.Note.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        }

        return moves.Select(m =>
        {
            var who = m.WorkerName.Length > 0 ? m.WorkerName : Loc.T("The till");
            var size = Math.Abs(m.Quantity);

            var sentence = m.Quantity >= 0m
                ? $"{who} put {size:0.###} onto {m.ProductName} — {m.BeforeQty:0.###} to {m.AfterQty:0.###}."
                : $"{who} took {size:0.###} off {m.ProductName} — {m.BeforeQty:0.###} to {m.AfterQty:0.###}.";

            if (m.Note.Length > 0) sentence += $" {m.Note}";

            return new Entry
            {
                At = m.MovedAt,
                Initial = who[..1].ToUpperInvariant(),
                Sentence = sentence,
                Kind = m.ReasonLabel,
                IsStock = true,
            };
        });
    }

    private void FillKindFilter()
    {
        if (KindFilter.ItemsSource is not null) return;

        _building = true;
        KindFilter.ItemsSource = Kinds;
        KindFilter.SelectedIndex = 0;
        _building = false;
    }

    private void ShowSummary()
    {
        var people = _rows.Count(e => !e.IsStock);
        var stock = _rows.Count - people;

        Summary.Text = _rows.Count == 0
            ? string.Empty
            : Loc.T("{0} changes · {1} stock movements · {2}",
                    people, stock, Loc.T(Dates.RangeLabel).ToLowerInvariant());
    }

    private void ShowEmptyState(string search)
    {
        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_rows.Count > 0) return;

        var filtered = search.Length > 0 || KindFilter.SelectedIndex > 0;

        EmptyTitle.Text = Loc.T(filtered ? "Nothing matches" : "Nothing happened");
        EmptyBody.Text = filtered
            ? "Try a different search, or another kind."
            : Loc.T("No changes and no stock moved {0}.",
                    Loc.T(Dates.RangeLabel).ToLowerInvariant());
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !_building) Refresh();
    }
}
