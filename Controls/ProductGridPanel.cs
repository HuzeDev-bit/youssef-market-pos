using System.Windows;
using System.Windows.Controls;

namespace MarketPos.Controls;

/// <summary>
/// Responsive product grid, in the shape professional touchscreen tills use: every tile is
/// the same size, the column count follows the available width, and the last row simply
/// stops where it stops — aligned left, never stretched to look "finished".
///
/// Height is deliberately derived from width via <see cref="Aspect"/> and never from the
/// viewport. An earlier version divided the screen height by the row count, which filled
/// the page but produced tall skinny tiles whenever there were only a couple of rows.
/// </summary>
public sealed class ProductGridPanel : Panel
{
    /// <summary>Preferred tile width. Column count is chosen to land near this.</summary>
    public double IdealCardWidth { get; set; } = 208;

    public double MinCardWidth { get; set; } = 170;
    public double MaxCardWidth { get; set; } = 250;

    /// <summary>Width ÷ height. 0.86 is a gently portrait tile: room for a big photo without going skinny.</summary>
    public double Aspect { get; set; } = 0.86;

    public double Gap { get; set; } = 16;

    private int _columns = 1;
    private Size _cardSize;

    private void CalculateLayout(double availableWidth, int count)
    {
        if (double.IsInfinity(availableWidth) || availableWidth <= 0) availableWidth = 1200;

        // Columns come from the WIDTH ONLY, never from the item count. Clamping to the count
        // was the bug behind giant tiles: a category holding three products produced three
        // columns, so each card stretched to a third of the screen.
        var columns = Math.Max(1, (int)Math.Floor((availableWidth + Gap) / (IdealCardWidth + Gap)));

        var cardWidth = (availableWidth - Gap * (columns - 1)) / columns;

        // Too narrow: drop a column so tiles stay readable.
        while (cardWidth < MinCardWidth && columns > 1)
        {
            columns--;
            cardWidth = (availableWidth - Gap * (columns - 1)) / columns;
        }

        // Too wide: keep the tile at its natural size and simply leave the rest of the row
        // empty. A handful of products should look like a handful, not fill the screen.
        if (cardWidth > MaxCardWidth) cardWidth = MaxCardWidth;

        _columns = columns;
        _cardSize = new Size(cardWidth, Math.Round(cardWidth / Aspect));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0) return new Size(0, 0);

        CalculateLayout(availableSize.Width, count);

        foreach (UIElement child in InternalChildren)
            child.Measure(_cardSize);

        var rows = (int)Math.Ceiling((double)count / _columns);
        var width = double.IsInfinity(availableSize.Width) ? _cardSize.Width * _columns : availableSize.Width;
        return new Size(width, rows * _cardSize.Height + Gap * (rows - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var column = i % _columns;
            var row = i / _columns;
            InternalChildren[i].Arrange(new Rect(
                column * (_cardSize.Width + Gap),
                row * (_cardSize.Height + Gap),
                _cardSize.Width,
                _cardSize.Height));
        }
        return finalSize;
    }
}
