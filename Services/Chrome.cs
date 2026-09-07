using System.Windows;
using System.Windows.Input;

namespace MarketPos.Services;

/// <summary>
/// Moving and sizing a window that has no title bar of its own.
///
/// Both windows are <c>WindowStyle="None"</c>, which is what makes them look like the rest of
/// the app rather than like Windows. It also throws away everything a title bar does for free:
/// dragging, double-click to restore, and the middle button of the three. Both shipped filling
/// the whole screen with no way to move them and no way to make them smaller — the app looked
/// like it had seized.
///
/// Filling the screen here means being sized to the work area by hand, never
/// <see cref="WindowState.Maximized"/>: a borderless maximised window overhangs the screen by
/// the invisible resize border, which pushes the window controls off the edge and swallows
/// their clicks.
/// </summary>
public static class Chrome
{
    /// <summary>How close to the work area still counts as filling it.</summary>
    private const double Slack = 2;

    /// <summary>
    /// Where the window sat before it was told to fill the screen, so that shrinking it puts
    /// it back where the user last left it. Attached rather than kept in a dictionary here,
    /// so it dies with the window instead of holding it alive.
    /// </summary>
    private static readonly DependencyProperty WindowedProperty =
        DependencyProperty.RegisterAttached("Windowed", typeof(Rect), typeof(Window),
                                            new PropertyMetadata(Rect.Empty));

    // ============================== Which state it is in ==============================

    /// <summary>
    /// How big the window is, asked in a way that works before it has been shown.
    ///
    /// <see cref="FrameworkElement.ActualWidth"/> is zero until the window is on screen, and
    /// <see cref="FrameworkElement.Width"/> is NaN until somebody sets it. Either one alone
    /// gives the wrong answer half the time; between them there is always a real number.
    /// </summary>
    private static Size Measure(Window window) => new(
        double.IsNaN(window.Width) ? window.ActualWidth : window.Width,
        double.IsNaN(window.Height) ? window.ActualHeight : window.Height);

    public static bool FillsTheScreen(Window window)
    {
        if (window.WindowState == WindowState.Maximized) return true;

        var area = SystemParameters.WorkArea;
        var size = Measure(window);

        return Math.Abs(window.Left - area.Left) < Slack
            && Math.Abs(window.Top - area.Top) < Slack
            && Math.Abs(size.Width - area.Width) < Slack
            && Math.Abs(size.Height - area.Height) < Slack;
    }

    /// <summary>Fills the work area — the desktop minus the taskbar.</summary>
    public static void Fill(Window window)
    {
        var was = Measure(window);
        if (window.WindowState == WindowState.Normal && !FillsTheScreen(window) && was.Width > 0)
            window.SetValue(WindowedProperty, new Rect(window.Left, window.Top, was.Width, was.Height));

        var area = SystemParameters.WorkArea;

        window.WindowState = WindowState.Normal;
        window.Left = area.Left;
        window.Top = area.Top;
        window.Width = area.Width;
        window.Height = area.Height;
    }

    /// <summary>
    /// Back to a window that can be moved around. Returns to wherever it was last put; the
    /// first time, to something comfortably smaller than the screen but still big enough to
    /// work in, centred so it cannot land half off the edge.
    /// </summary>
    public static void Shrink(Window window)
    {
        var area = SystemParameters.WorkArea;
        var last = (Rect)window.GetValue(WindowedProperty);

        var width = Math.Min(last.Width > 400 ? last.Width : Math.Round(area.Width * 0.8), area.Width);
        var height = Math.Min(last.Height > 300 ? last.Height : Math.Round(area.Height * 0.86), area.Height);

        var left = last.Width > 400 ? last.X : area.Left + (area.Width - width) / 2;
        var top = last.Height > 300 ? last.Y : area.Top + (area.Height - height) / 2;

        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;

        // Clamped, because a remembered position is only good until somebody unplugs a monitor.
        window.Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width));
        window.Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height));

        window.SetValue(WindowedProperty, new Rect(window.Left, window.Top, width, height));
    }

    public static void Toggle(Window window)
    {
        if (FillsTheScreen(window)) Shrink(window); else Fill(window);
    }

    // ============================== Dragging it about ==============================

    /// <summary>
    /// What a title bar does when you press it: double-click to switch between filling the
    /// screen and not, and a press-and-hold to move the window.
    ///
    /// A window filling the screen shrinks first, the way every other Windows application
    /// does — dragging the top of a maximised window is how most people un-maximise one, and
    /// the old handler simply refused, which is what "it will not budge" was.
    /// </summary>
    public static void Drag(Window window, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Toggle(window);
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed) return;

        if (FillsTheScreen(window)) Shrink(window);

        // Throws if the button came up between the event and here — a fast click, or the
        // window being moved by something else. Nothing to do about it, and nothing to say.
        try { window.DragMove(); }
        catch (InvalidOperationException) { }
    }
}
