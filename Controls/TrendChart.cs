using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MarketPos.Services;

namespace MarketPos.Controls;

public enum ChartStyle
{
    Bars,
    Line,
    Area,
}

/// <summary>
/// A small chart drawn directly, with no charting library.
///
/// Everything the owner needs from a chart here is shape and scale — is trade rising, which
/// day was the good one — and that is a few dozen lines of geometry. Pulling in a charting
/// package would add megabytes to a till that runs offline on an old machine, and would bring
/// its own colours and fonts to fight with the brand.
///
/// Two series can be drawn together (sales against expenses); the second is drawn in the
/// muted grey so the primary line stays the one the eye lands on.
/// </summary>
public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<Finance.Point>), typeof(TrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ComparePointsProperty = DependencyProperty.Register(
        nameof(ComparePoints), typeof(IReadOnlyList<Finance.Point>), typeof(TrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StyleKindProperty = DependencyProperty.Register(
        nameof(StyleKind), typeof(ChartStyle), typeof(TrendChart),
        new FrameworkPropertyMetadata(ChartStyle.Bars, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// False draws the shape alone: no gridlines, no value scale, no dates. For a chart small
    /// enough that the only question it answers is "which way is trade going", where an axis
    /// would take half the room and be unreadable at the size it was left.
    /// </summary>
    public static readonly DependencyProperty ShowScaleProperty = DependencyProperty.Register(
        nameof(ShowScale), typeof(bool), typeof(TrendChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(TrendChart),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Finance.Point>? Points
    {
        get => (IReadOnlyList<Finance.Point>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IReadOnlyList<Finance.Point>? ComparePoints
    {
        get => (IReadOnlyList<Finance.Point>?)GetValue(ComparePointsProperty);
        set => SetValue(ComparePointsProperty, value);
    }

    public ChartStyle StyleKind
    {
        get => (ChartStyle)GetValue(StyleKindProperty);
        set => SetValue(StyleKindProperty, value);
    }

    public bool ShowScale
    {
        get => (bool)GetValue(ShowScaleProperty);
        set => SetValue(ShowScaleProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    // The theme's own border and muted ink, so the chart's furniture matches the cards it
    // sits in rather than being a second grey.
    private static readonly Brush Grid = new SolidColorBrush(Color.FromRgb(0xE6, 0xEB, 0xE9));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x5F, 0x72, 0x69));

    // FormattedText does not inherit the window's font, so an axis drawn with the default
    // typeface would be the one piece of Segoe UI left in a branded app. Same pack URI the
    // theme uses; if the resource ever fails to resolve WPF falls back on its own.
    private static readonly Typeface Face =
        new(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private const double AxisHeight = 20;   // room for the date labels under the plot
    private const double LabelWidth = 46;   // room for the value scale on the left
    private const int Rules = 4;            // gridlines above the baseline

    static TrendChart()
    {
        Grid.Freeze();
        Muted.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var points = Points;
        var floor = ShowScale ? 50 : 24;
        if (points is null || points.Count == 0 || ActualWidth < 60 || ActualHeight < floor) return;

        var plot = ShowScale
            ? new Rect(LabelWidth, 4,
                       Math.Max(1, ActualWidth - LabelWidth - 4),
                       Math.Max(1, ActualHeight - AxisHeight - 4))
            : new Rect(0, 2, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight - 4));

        var peak = points.Select(p => p.Value)
                         .Concat(ComparePoints?.Select(p => p.Value) ?? Enumerable.Empty<decimal>())
                         .DefaultIfEmpty(0m).Max();

        // The scale is built from the step up, not from the top down. Rounding the top to a
        // nice number and then quartering it gives fractional gridlines — 0, 0.25, 0.5 — which
        // the whole-number labels printed as "0 0 1 1 1", four ticks claiming two values.
        // A whole step, four times, means every line reads as a different number.
        var step = NiceStep(peak <= 0m ? 1d : (double)peak / Rules);
        var top = step * Rules;

        if (ShowScale) DrawGrid(dc, plot, step);

        if (ComparePoints is { Count: > 0 })
            DrawSeries(dc, plot, ComparePoints, top, Muted, ChartStyle.Line, thin: true);

        DrawSeries(dc, plot, points, top, Accent, StyleKind, thin: false);
        if (ShowScale) DrawAxis(dc, plot, points);
    }

    /// <summary>Four horizontal rules with their values, which is enough to read a level off.</summary>
    private void DrawGrid(DrawingContext dc, Rect plot, double step)
    {
        var pen = new Pen(Grid, 1);
        pen.Freeze();

        for (var i = 0; i <= Rules; i++)
        {
            var y = Snap(plot.Bottom - plot.Height * i / Rules);
            dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));

            // A step of 1.5 needs a decimal or three ticks all print as "2".
            var label = Short(step * i, step % 1 == 0 ? 0 : 1);
            var text = Text(label, 10, Muted);
            dc.DrawText(text, new Point(plot.Left - 8 - text.Width, y - text.Height / 2));
        }
    }

    private void DrawSeries(DrawingContext dc, Rect plot, IReadOnlyList<Finance.Point> points,
                            double top, Brush brush, ChartStyle style, bool thin)
    {
        var step = plot.Width / points.Count;

        if (style == ChartStyle.Bars)
        {
            // Bars get a gap of a quarter of their slot, and a floor of 2px so a small but
            // non-zero day is still visibly different from a day with no trade at all.
            var width = Math.Max(2, step * 0.62);
            for (var i = 0; i < points.Count; i++)
            {
                var value = (double)points[i].Value;
                if (value <= 0) continue;

                var height = Math.Max(2, plot.Height * value / top);
                var x = plot.Left + step * i + (step - width) / 2;
                var rect = new Rect(x, plot.Bottom - height, width, height);
                dc.DrawRoundedRectangle(brush, null, rect, Math.Min(4, width / 2), Math.Min(4, width / 2));
            }
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = true;
            for (var i = 0; i < points.Count; i++)
            {
                var value = (double)points[i].Value;
                var x = X(plot, i, points.Count);
                var y = plot.Bottom - plot.Height * Math.Min(1, value / top);

                if (first)
                {
                    ctx.BeginFigure(new Point(x, y), isFilled: style == ChartStyle.Area, isClosed: false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: true);
                }

                if (style == ChartStyle.Area && i == points.Count - 1)
                {
                    ctx.LineTo(new Point(x, plot.Bottom), false, false);
                    ctx.LineTo(new Point(X(plot, 0, points.Count), plot.Bottom), false, false);
                }
            }
        }
        geometry.Freeze();

        if (style == ChartStyle.Area)
        {
            var fill = brush.Clone();
            fill.Opacity = 0.16;
            fill.Freeze();
            dc.DrawGeometry(fill, null, geometry);
        }

        var pen = new Pen(brush, thin ? 1.5 : 2.2)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        if (thin) pen.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// Date labels along the bottom, thinned until they fit. Drawing every label on a
    /// 90-day range would produce a grey smear rather than an axis.
    /// </summary>
    private void DrawAxis(DrawingContext dc, Rect plot, IReadOnlyList<Finance.Point> points)
    {
        var step = plot.Width / points.Count;
        var every = Math.Max(1, (int)Math.Ceiling(52 / Math.Max(1, step)));

        for (var i = 0; i < points.Count; i++)
        {
            if (i % every != 0 && i != points.Count - 1) continue;

            var text = Text(points[i].Label, 10, Muted);
            var x = X(plot, i, points.Count) - text.Width / 2;
            if (x + text.Width > plot.Right + 2) continue;   // never clip the last label
            dc.DrawText(text, new Point(Math.Max(plot.Left, x), plot.Bottom + 5));
        }
    }

    /// <summary>
    /// Where a point sits across the plot. Bars own a slot each and stand in the middle of it;
    /// a line or an area is a shape, and a shape that stops half a slot short of both ends
    /// reads as a chart that has been squeezed rather than one drawn to fit.
    /// </summary>
    private double X(Rect plot, int index, int count) =>
        StyleKind == ChartStyle.Bars || count < 2
            ? plot.Left + plot.Width / count * index + plot.Width / count / 2
            : plot.Left + plot.Width * index / (count - 1);

    private static FormattedText Text(string value, double size, Brush brush) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush,
            VisualTreeHelper.GetDpi(new System.Windows.Controls.Border()).PixelsPerDip);

    /// <summary>
    /// Rounds one gridline's worth up to a number a person would have chosen, and never below
    /// one whole dirham — a scale marked in quarter-dirhams is not a scale a shopkeeper reads.
    /// The finer multiples keep the top of the chart close to the tallest bar; with only
    /// 1, 2, 5, 10 to choose from, a peak of 137 was drawn on a scale that ran to 200.
    /// </summary>
    private static double NiceStep(double value)
    {
        if (value <= 1) return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var multiple in new[] { 1, 1.5, 2, 2.5, 3, 4, 5, 7.5, 10 })
        {
            var candidate = magnitude * multiple;
            if (candidate >= value) return Math.Max(1, Math.Round(candidate, 2));
        }
        return magnitude * 10;
    }

    private static string Short(double value, int decimals = 0) => value switch
    {
        >= 1_000_000 => (value / 1_000_000).ToString("0.#") + "M",
        >= 1_000 => (value / 1_000).ToString("0.#") + "k",
        _ => value.ToString("F" + decimals),
    };

    /// <summary>Puts a 1px rule on a device pixel so it renders crisp rather than as a grey smudge.</summary>
    private static double Snap(double y) => Math.Round(y) + 0.5;
}
