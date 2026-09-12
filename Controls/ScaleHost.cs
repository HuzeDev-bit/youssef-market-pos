using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MarketPos.Controls;

/// <summary>
/// Draws its contents at whatever size the screen actually is.
///
/// The app was laid out on a 1920×1080 monitor, and every measurement in it — a 36px row, a
/// 222px sidebar, 11pt small print — is a number typed against that screen. Hand the same
/// window to a shop with a 1366×768 laptop and none of those numbers change: the furniture
/// stays the size it was and simply runs off the bottom.
///
/// Squeezing the layout instead is not an answer either. A table narrower than its columns
/// does not become a smaller table, it becomes a column of ellipses; a dashboard shorter than
/// its panels just loses the last one. Both are the failure people mean when they say a
/// desktop app "doesn't fit".
///
/// So the whole interface is treated the way a phone treats a screen: it is drawn at one
/// size and then scaled to the one in front of it. Every proportion the designer chose
/// survives, and the thing that changes is the thing that should — how big it all is.
///
/// <para>
/// The scale is never above 1. <see cref="DesignWidth"/> and <see cref="DesignHeight"/> are
/// the smallest size the content is honestly usable at, not the size it was drawn at, so a
/// screen with room to spare is left completely alone and only a screen that is genuinely
/// short of room gives anything up — and then only exactly as much as it has to.
/// </para>
/// </summary>
public sealed class ScaleHost : Decorator
{
    /// <summary>The room the content needs before anything has to shrink. 0 = no limit in that direction.</summary>
    public double DesignWidth { get; init; }
    public double DesignHeight { get; init; }

    /// <summary>
    /// How far this is willing to go. Past this the text stops being readable, and a screen
    /// that small is better served by a scrollbar than by a magnifying glass.
    /// </summary>
    public double MinScale { get; init; } = 0.55;

    public double Scale { get; private set; } = 1.0;

    /// <summary>
    /// Raised when the scale changes. The window chrome listens: the caption strip Windows
    /// drags the window by is measured in real screen pixels, not in the content's
    /// coordinates, so it has to shrink alongside the header it covers.
    /// </summary>
    public event Action<double>? Rescaled;

    protected override Size MeasureOverride(Size available)
    {
        var child = Child;
        if (child is null) return default;

        Rescale(ScaleFor(available));

        // Measured with the real room, not the scaled room: a LayoutTransform already divides
        // the constraint by itself on the way in and multiplies the answer on the way out, so
        // the child is asked for a layout in its own coordinates and replies in ours.
        child.Measure(available);
        return child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private double ScaleFor(Size room)
    {
        var scale = 1.0;

        if (DesignWidth > 0 && room.Width > 0 && !double.IsInfinity(room.Width))
            scale = Math.Min(scale, room.Width / DesignWidth);

        if (DesignHeight > 0 && room.Height > 0 && !double.IsInfinity(room.Height))
            scale = Math.Min(scale, room.Height / DesignHeight);

        return Math.Clamp(scale, MinScale, 1.0);
    }

    /// <summary>
    /// Rounded to the nearest half percent, and skipped entirely when nothing moved.
    ///
    /// Changing the child's transform invalidates it, which asks for another measure — so an
    /// unrounded scale would have a window dragged one pixel wider re-laying out the entire
    /// app, and a scale that jittered in the last decimal place would never settle at all.
    /// </summary>
    private void Rescale(double scale)
    {
        scale = Math.Round(scale, 3);
        if (Math.Abs(scale - Scale) < 0.0005) return;

        Scale = scale;

        // A LayoutTransform, not a RenderTransform. This one lays the content out in its own
        // full-size coordinates and then draws the result smaller, which is the whole idea.
        // A RenderTransform would lay the content out for the small screen first and then
        // shrink that, taking the same room off it twice.
        if (Child is FrameworkElement child)
            child.LayoutTransform = scale >= 1.0
                ? Transform.Identity
                : new ScaleTransform(scale, scale);

        Rescaled?.Invoke(scale);
    }
}
