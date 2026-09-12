using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using MarketPos.Controls;

namespace MarketPos.Services;

/// <summary>
/// Makes the app the size of the screen it is running on.
///
/// Every measurement in this codebase — a 36px row, a 222px sidebar, a 940px delivery dialog,
/// 11pt small print — was typed against the 1920×1080 monitor it was built on. None of those
/// numbers know that. Give the same build to a shop with a 1366×768 laptop and the furniture
/// stays exactly the size it was: the back office loses the bottom of every page, the Save
/// button on Add supplier sits below the screen, and the owner concludes the app is broken.
///
/// The fix is the one a phone uses between a small handset and a large one. The interface is
/// drawn at one size and then scaled to the screen in front of it, so every proportion the
/// design has survives and the only thing that changes is how big it all is. See
/// <see cref="ScaleHost"/> for why scaling rather than squeezing or scrolling.
///
/// <para>
/// Nothing shrinks that does not have to. The scale is capped at 1, and it is measured against
/// the smallest size the content is honestly usable at — not against the monitor it was drawn
/// on — so a screen with room to spare gets exactly the app that was designed, unchanged, and
/// a screen that is short of room gives up precisely the difference and not a pixel more.
/// </para>
/// </summary>
public static class Responsive
{
    /// <summary>Room left around a dialog, so it never sits flush against the screen edge.</summary>
    private const double Breathing = 48;

    /// <summary>
    /// Where shrinking a dialog stops being the right answer and a scrollbar becomes it.
    ///
    /// The line falls between a form that is somewhat too tall for the window and one that is
    /// most of twice its height. Add supplier needs 65% to fit a 900x600 back office and is
    /// far better shrunk — the whole form, Save included, on screen at once. Settings needs
    /// 53%, which is not a form that is slightly too tall, and is far better scrolled.
    /// </summary>
    private const double Legible = 0.6;

    // ======================= The two full-screen shells =======================

    /// <summary>
    /// Wraps a window that fills the screen — the till and the back office — so its whole
    /// interior scales together: rail, header, sidebar, tables, cart and page as one piece.
    ///
    /// <paramref name="designWidth"/> and <paramref name="designHeight"/> are the smallest
    /// size that window's contents are genuinely usable at, measured in the coordinates the
    /// XAML is written in. Above that size nothing happens at all.
    /// </summary>
    public static void Shell(Window window, double designWidth, double designHeight)
    {
        if (window.Content is not UIElement content) return;

        var host = new ScaleHost
        {
            DesignWidth = designWidth,
            DesignHeight = designHeight,
        };

        // Detached first: an element cannot be handed to a new parent while the old one still
        // claims it, and the window is still the content's parent until this is cleared.
        window.Content = null;
        host.Child = content;
        window.Content = host;

        FollowWithTheCaption(window, host);
        FollowTheScreen(window);
    }

    /// <summary>
    /// Keeps a shell on the screen when the screen itself changes underneath it.
    ///
    /// A monitor is not a constant. The resolution gets changed, a laptop is undocked at the
    /// end of the day, a projector is unplugged — and a window sized to yesterday's screen
    /// simply hangs off the edge of today's, with the window buttons somewhere past the
    /// corner. The contents already scale to the window; this is what makes the window itself
    /// follow the screen.
    /// </summary>
    private static void FollowTheScreen(Window window)
    {
        void Changed(object? sender, EventArgs e) =>
            window.Dispatcher.BeginInvoke(() => KeepOnTheScreen(window));

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += Changed;
        window.Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= Changed;
    }

    private static void KeepOnTheScreen(Window window)
    {
        if (window.WindowState != WindowState.Normal) return;

        var area = SystemParameters.WorkArea;

        // Bigger than the screen now is: fill it, which is where a till belongs anyway.
        if (window.ActualWidth > area.Width + 1 || window.ActualHeight > area.Height + 1)
        {
            Chrome.Fill(window);
            return;
        }

        // Otherwise it only has to be pulled back inside the edges.
        window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.ActualWidth));
        window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.ActualHeight));
    }

    /// <summary>
    /// The scale a window is currently drawn at — 1 for anything that is not a scaled shell,
    /// including a null window, so a dialog with no owner is simply left at full size.
    /// </summary>
    public static double ScaleOf(Window? window) =>
        window?.Content is ScaleHost host ? host.Scale : 1.0;

    /// <summary>
    /// Keeps the draggable strip along the top in step with the header underneath it.
    ///
    /// Both shells hand their top strip to Windows as the window's caption, which is what
    /// makes them drag and snap like any other window. That strip is declared in real screen
    /// pixels and knows nothing about the scale, so a header shrunk to 78% would leave a
    /// caption 96px tall lying over the page below it — swallowing the clicks of whatever it
    /// happened to cover.
    /// </summary>
    private static void FollowWithTheCaption(Window window, ScaleHost host)
    {
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome is null) return;

        if (chrome.IsFrozen)
        {
            chrome = (WindowChrome)chrome.Clone();
            WindowChrome.SetWindowChrome(window, chrome);
        }

        var designed = chrome.CaptionHeight;
        host.Rescaled += scale => chrome.CaptionHeight = Math.Round(designed * scale);
    }

    // ============================== Dialogs ==============================

    /// <summary>
    /// Fits a dialog to the window it belongs to.
    ///
    /// These windows size themselves to their contents, which is right on a big monitor and
    /// wrong on a small one: Record a delivery is 940px wide, and Add supplier carries a
    /// contact form and a goods editor, so on a 768-pixel laptop the bottom of it — what was
    /// paid, and the Save button — was simply off the screen with no way to reach it.
    ///
    /// So the dialog is measured at the size it wants to be, and if that does not fit, the
    /// whole thing is scaled down until it does. One that already fits is left untouched.
    /// </summary>
    public static void Fit(Window window)
    {
        // Once, and on load rather than now. The natural size this works from is the one the
        // dialog settles at with its data in it, which is not known while it is still being
        // constructed — and it is only worth measuring before anything here has interfered.
        void Once(object? sender, RoutedEventArgs e)
        {
            window.Loaded -= Once;

            FitTo(window, RoomFor(window));

            window.UpdateLayout();
            Centre(window);
        }

        window.Loaded += Once;
    }

    /// <summary>
    /// How much room a dialog actually has, which is the window it opens over and not the
    /// screen that window happens to be sitting on.
    ///
    /// This is the difference between a dialog that is responsive and one that merely fits.
    /// The shells fill the screen by default, so most of the time these are the same number —
    /// but pull the back office down to half the monitor and the screen stops being the
    /// answer. Settings would go on opening at its full 500x1042 over a 700px window, hanging
    /// off both ends of the thing it belongs to, because the screen behind it had room.
    /// </summary>
    public static Size RoomFor(Window window)
    {
        var area = SystemParameters.WorkArea;
        var screen = new Size(area.Width, area.Height);

        if (window.Owner is not { WindowState: not WindowState.Minimized } owner)
            return screen;

        // ActualWidth is zero until a window is on screen and Width is NaN until somebody
        // sets it; between the two there is always a real number.
        var width = double.IsNaN(owner.Width) ? owner.ActualWidth : owner.Width;
        var height = double.IsNaN(owner.Height) ? owner.ActualHeight : owner.Height;

        if (width <= 0 || height <= 0) return screen;

        // Never larger than the screen either: an owner can be dragged partly off the edge.
        return new Size(Math.Min(width, screen.Width), Math.Min(height, screen.Height));
    }

    /// <summary>
    /// Fits a dialog into a stated amount of room, and answers with the size it ends up.
    ///
    /// Split out from <see cref="Fit"/> so the diagnostics can stand a dialog on a screen the
    /// machine running them does not have. A check that only passes on the monitor it was
    /// written on is not a check — which is the whole reason any of this exists.
    /// </summary>
    public static Size FitTo(Window window, Size room)
    {
        if (window.Content is not FrameworkElement root) return room;

        var roomWidth = Room(room.Width, 320);
        var roomHeight = Room(room.Height);

        var natural = NaturalSize(window, root, out var chrome);

        // A dialog is never drawn larger than the window it belongs to is drawn. Without this
        // a back office scaled to 80% would raise dialogs at 100%, so the writing on the form
        // in front would be bigger than the writing on the page behind it.
        var shell = ScaleOf(window.Owner);

        var forWidth = Math.Min(shell, roomWidth / natural.Width);
        var forHeight = Math.Min(shell, roomHeight / natural.Height);

        // Shrinking to fit the height is the right answer while it stays a modest adjustment.
        // Past that the dialog is not slightly too big — it is a long form on a short screen,
        // and the answer to that is a scrollbar.
        //
        // Settings is the case that decides this. It is 500 wide and 1042 tall, so on a shop
        // laptop it would have to come down to 54% to fit: 275px of dialog on a screen with
        // 1045px going spare, small print at 6pt, and — because 54% is past what is legible —
        // still scrolling afterwards. Left at full width and scrolled, every word of it reads
        // exactly as it was written.
        var scale = forHeight < Legible ? forWidth : Math.Min(forWidth, forHeight);

        if (scale < 1.0) Shrink(window, root, natural, chrome, scale);

        // A backstop rather than the mechanism. The scaling above is what makes a dialog fit;
        // this only catches one that grows after the fact — a delivery gaining rows as goods
        // are added to it — and stops it growing off the screen.
        window.MaxWidth = Math.Min(window.MaxWidth, roomWidth);
        window.MaxHeight = Math.Min(window.MaxHeight, roomHeight);

        var wanted = new Size(natural.Width * scale, natural.Height * scale);

        // Whatever is left over has to be reachable somehow.
        if (wanted.Height > roomHeight) AddAScrollbar(window);

        return new Size(Math.Min(wanted.Width, roomWidth), Math.Min(wanted.Height, roomHeight));
    }

    private static double Room(double screen, double least = 240) =>
        Math.Max(least, screen - Breathing);

    /// <summary>
    /// The size the dialog would be if the screen were not a consideration.
    ///
    /// Measured off the content rather than off the window, because the window has already
    /// been capped: three of these dialogs declare a MaxHeight in their XAML, so asking the
    /// window how tall it is returns the cap and not the truth. Measuring the content with no
    /// ceiling at all is the only way to learn how much there really is to fit.
    ///
    /// <paramref name="chrome"/> comes back as the difference between the two — the title bar
    /// and borders Windows draws around the content, which do not scale with it.
    /// </summary>
    private static Size NaturalSize(Window window, FrameworkElement root, out Size chrome)
    {
        chrome = new Size(Math.Max(0, window.ActualWidth - root.ActualWidth),
                          Math.Max(0, window.ActualHeight - root.ActualHeight));

        var declaredWidth = double.IsNaN(window.Width) ? 0 : window.Width;

        root.Measure(new Size(
            declaredWidth > 0 ? Math.Max(1, declaredWidth - chrome.Width) : double.PositiveInfinity,
            double.PositiveInfinity));
        var wanted = root.DesiredSize;

        // Put back, or the arrange pass that follows would work from a measurement taken
        // against a constraint the window never actually offered.
        root.InvalidateMeasure();

        // The larger of what it asks for and what it declares. A dialog with a fixed height
        // wants less than it reserves — the receipt preview stretches to fill its 620px — and
        // scaling to what it asked for would leave the window at its full declared height.
        return new Size(
            Math.Max(declaredWidth, wanted.Width + chrome.Width),
            Math.Max(double.IsNaN(window.Height) ? 0 : window.Height, wanted.Height + chrome.Height));
    }

    private static void Shrink(Window window, FrameworkElement root, Size natural, Size chrome, double scale)
    {
        root.LayoutTransform = new ScaleTransform(scale, scale);

        window.Width = Math.Round((natural.Width - chrome.Width) * scale) + chrome.Width;

        // Height only where the dialog states one. The rest size themselves to their contents,
        // and the contents have just become shorter, so they follow on their own.
        if (window.SizeToContent is not (SizeToContent.Height or SizeToContent.WidthAndHeight))
            window.Height = Math.Round((natural.Height - chrome.Height) * scale) + chrome.Height;
        else if (!double.IsInfinity(window.MaxHeight))
            window.MaxHeight = Math.Round((window.MaxHeight - chrome.Height) * scale) + chrome.Height;
    }

    /// <summary>
    /// For the part of a dialog that scaling could not win back — a form longer than the
    /// screen is tall, which no amount of shrinking fixes without making it unreadable.
    ///
    /// Only the window's own content counts as already scrolling. This used to search the
    /// whole tree, and that is why Add supplier still ran off the bottom of a shop laptop
    /// with Save out of reach: the dialog holds a list of delivered goods, that list scrolls,
    /// and finding it was taken as proof that the dialog scrolled. It did not — a scroller
    /// three levels down moves its own rows and nothing else.
    /// </summary>
    private static void AddAScrollbar(Window window)
    {
        if (window.Content is not UIElement content || Scrolls(content)) return;

        window.Content = null;
        window.Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,

            // The dialog's own card supplies the padding and the background; the scroller is
            // only there to move it, so it brings nothing of its own.
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
    }

    /// <summary>
    /// True when this element moves everything below it — itself a scroller, or a plain
    /// wrapper around one. A Border around a ScrollViewer scrolls in every way that matters;
    /// a Grid with a scroller in one of its cells does not.
    /// </summary>
    private static bool Scrolls(object? node) => node switch
    {
        ScrollViewer => true,
        Decorator wrapper => Scrolls(wrapper.Child),
        ContentControl wrapper => Scrolls(wrapper.Content),
        _ => false,
    };

    /// <summary>
    /// Puts the dialog back in the middle of whatever it was centred on, and inside the
    /// screen either way. Windows placed it before any of this changed its size, and a
    /// remembered position is only good until somebody unplugs a monitor.
    /// </summary>
    private static void Centre(Window window)
    {
        var area = SystemParameters.WorkArea;

        var (left, top) = window.Owner is { WindowState: WindowState.Normal } owner
            ? (owner.Left + (owner.ActualWidth - window.ActualWidth) / 2,
               owner.Top + (owner.ActualHeight - window.ActualHeight) / 2)
            : (area.Left + (area.Width - window.ActualWidth) / 2,
               area.Top + (area.Height - window.ActualHeight) / 2);

        window.Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - window.ActualWidth));
        window.Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - window.ActualHeight));
    }
}
