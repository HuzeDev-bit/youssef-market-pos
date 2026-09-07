using System.Windows;
using System.Windows.Controls;

namespace MarketPos.Services;

/// <summary>
/// Keeps a dialog inside the screen it is opened on.
///
/// Most of these windows are <c>SizeToContent="Height"</c>: they grow to fit whatever is in
/// them, which is right on the developer's monitor and wrong on a shop machine. Add supplier
/// carries a contact form and a goods editor, and on a 768-pixel laptop the bottom of it —
/// what was paid, and the Save button — was simply off the screen with no way to reach it.
///
/// Two rules, applied to every window as it is built. It may never be taller or wider than the
/// screen's working area, which is the desktop minus the taskbar. And if capping it would cut
/// anything off, the content goes inside a scroller so the rest can still be reached.
/// </summary>
public static class Responsive
{
    /// <summary>Room left around the window, so it never sits flush against the screen edge.</summary>
    private const double Breathing = 48;

    public static void Fit(Window window)
    {
        var area = SystemParameters.WorkArea;

        window.MaxHeight = Math.Max(320, area.Height - Breathing);
        window.MaxWidth = Math.Max(360, area.Width - Breathing);

        // A window already built to scroll needs nothing more; nesting a second scroller
        // inside the first gives two scrollbars and a wheel that moves the wrong one.
        if (window.Content is not FrameworkElement content || AlreadyScrolls(content)) return;

        window.Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,

            // The dialog's own card supplies the padding and the background; the scroller is
            // only there to move it, so it brings nothing of its own.
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
    }

    /// <summary>True when this window already puts its content inside a scroller.</summary>
    private static bool AlreadyScrolls(DependencyObject node)
    {
        if (node is ScrollViewer) return true;

        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject element && AlreadyScrolls(element)) return true;

        return false;
    }
}
