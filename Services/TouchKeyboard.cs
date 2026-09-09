using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MarketPos.Views;

namespace MarketPos.Services;

/// <summary>
/// The on-screen keyboard: when it exists, when it shows, and how it knows the shop wants to
/// type something.
///
/// A till on a touchscreen has no keyboard on the counter, and half of what this app does
/// needs one — a product name, a supplier, a price, an address. Windows has its own touch
/// keyboard, but a shop machine locked down to run one program is exactly the machine where
/// it has been turned off, so this app carries its own.
///
/// Two floating windows, both of which sit above the modal dialogs where most of the typing
/// happens, and neither of which ever takes focus from the box being filled in — see
/// <see cref="Floating"/>. The button is always in the corner so it can be found without
/// looking for it; the keyboard comes up when it is pressed.
/// </summary>
public static class TouchKeyboard
{
    private static KeyboardButton? _button;
    private static KeyboardWindow? _keys;
    private static bool _started;

    public static bool IsOpen => _keys is { IsVisible: true };

    // ============================== Whether to have one at all ==============================

    /// <summary>
    /// Whether this machine gets an on-screen keyboard.
    ///
    /// The shop's own answer if it has given one in Settings, and otherwise the machine's:
    /// a keyboard on a touchscreen till, and nothing at all on a counter that has a real one
    /// plugged into it. A shop that never opens Settings gets the right thing either way,
    /// which is the point — most of them never will.
    /// </summary>
    public static bool Wanted => AppSettings.Current.OnScreenKeyboard ?? HasATouchscreen;

    /// <summary>How many fingers this screen can register. Zero means it is not a touchscreen.</summary>
    public static bool HasATouchscreen
    {
        get
        {
            try { return GetSystemMetrics(MaximumTouches) > 0; }
            catch { return false; }
        }
    }

    private const int MaximumTouches = 95;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    // ============================== Life ==============================

    /// <summary>
    /// Called once, from the till, after the interface is on screen. Not from App.OnStartup:
    /// the diagnostics build every window in this app without ever showing one, and a floating
    /// keyboard on a headless run would be a window nobody asked for that nothing closes.
    /// </summary>
    public static void Start()
    {
        if (_started || !Wanted) return;
        _started = true;

        _button = new KeyboardButton();
        _button.Show();

        WatchWhatHasFocus();
        HideWhileAnotherAppIsInFront();
    }

    public static void Stop()
    {
        _keys?.Close();
        _button?.Close();
        _keys = null;
        _button = null;
        _started = false;
    }

    /// <summary>
    /// Turned on or off in Settings while the app is running, rather than at the next restart.
    /// A shop that ticks the box wants to see what it does.
    /// </summary>
    public static void Reconsider()
    {
        if (Wanted && !_started) Start();
        else if (!Wanted && _started) Stop();
    }

    // ============================== Showing the keys ==============================

    public static void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public static void Open(object? forThis = null)
    {
        if (!_started) return;

        _keys ??= new KeyboardWindow();
        _keys.SlideIn(OnlyTakesNumbers(forThis ?? Keyboard.FocusedElement));
    }

    /// <summary>
    /// Whether the box being filled in refuses everything that is not a number.
    ///
    /// Read off the box itself rather than kept in a list here: a price box says so in markup,
    /// with the attached property that does the refusing, and a box added to a form next year
    /// says so by the same line that makes it a price box. Nothing has to be told twice.
    /// </summary>
    private static bool OnlyTakesNumbers(object? element) =>
        element is TextBox box && Views.Admin.Numeric.GetOnly(box);

    public static void Close() => _keys?.SlideOut();

    // ============================== Knowing when it is wanted ==============================

    /// <summary>
    /// Lights the button up the moment the shop taps into something it can type in.
    ///
    /// A class handler rather than anything wired per-box, because the boxes worth catching
    /// are spread over twenty windows and a dozen data templates, and one added next year
    /// would otherwise be the one that is missed. Every text box in the process announces
    /// itself here, including the ones inside a dialog that does not exist yet.
    /// </summary>
    /// <summary>
    /// Something the shop types into, as opposed to something that merely looks like it.
    ///
    /// A date is chosen from a calendar, and WPF builds its field out of a TextBox like any
    /// other — so without this the keys came up over a date picker, where there is nothing to
    /// type and the calendar is what the shop actually wanted. A read-only box is out for the
    /// same reason: it can hold the caret and take none of the keys.
    /// </summary>
    private static bool TypedInto(object? element) => element switch
    {
        DatePickerTextBox => false,
        TextBox { IsReadOnly: true } => false,
        TextBoxBase or PasswordBox => true,
        _ => false,
    };

    private static void WatchWhatHasFocus()
    {
        foreach (var box in new[] { typeof(TextBoxBase), typeof(PasswordBox) })
        {
            // Pressing a box is what raises the keys. Not focus — the till puts the cursor
            // back in the barcode box every time the window is activated, and a keyboard that
            // came up for that would sit over the product grid from the moment the shop opened.
            // Pressing one is the shop saying it wants to type; focus is only the app tidying
            // up after itself.
            EventManager.RegisterClassHandler(box, UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler((sender, _) => { if (TypedInto(sender)) Open(sender); }));

            EventManager.RegisterClassHandler(box, UIElement.PreviewTouchDownEvent,
                new EventHandler<TouchEventArgs>((sender, _) => { if (TypedInto(sender)) Open(sender); }));

            EventManager.RegisterClassHandler(box, UIElement.PreviewStylusDownEvent,
                new StylusDownEventHandler((sender, _) => { if (TypedInto(sender)) Open(sender); }));

            // The button lights up for any focus at all, pressed or not, so it is obvious where
            // the keys come from on the one screen where they did not appear by themselves.
            EventManager.RegisterClassHandler(box, UIElement.GotKeyboardFocusEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (!TypedInto(sender)) return;

                    _button?.Wake();

                    // Already up, and the caret has landed in a box that only takes numbers:
                    // show the number pad without waiting to be asked.
                    //
                    // One way only. The caret moves on its own all the time at a till — Enter
                    // on a quantity commits it and hands the caret back to the barcode box,
                    // which takes letters because it doubles as the search box — and a
                    // keyboard that turned back into forty letters every time the cashier
                    // finished typing a number is a keyboard that is never the one wanted.
                    // Going back to letters is the shop's decision: press into a box that
                    // takes them, or press the keyboard key on the pad.
                    if (IsOpen && OnlyTakesNumbers(sender)) Open(sender);
                }));
        }

        // A press on anything that is not typed into puts the keys away.
        //
        // Focus alone does not cover this: empty space takes no focus, so pressing the middle
        // of a page left the caret exactly where it was and the keyboard sitting over the
        // screen with nothing to do. This is the gesture people already expect — tap away from
        // the writing and the keyboard goes.
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler((window, e) => PressedSomethingElse(window, e.OriginalSource)));

        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewTouchDownEvent,
            new EventHandler<TouchEventArgs>((window, e) => PressedSomethingElse(window, e.OriginalSource)));

        // Whatever takes focus, not just a text box losing it. A dialog opening puts the caret
        // on a dropdown or a button, and that is exactly the moment the keys have to get out of
        // the way — they were sitting over the admin dialog's own buttons.
        EventManager.RegisterClassHandler(typeof(UIElement), UIElement.GotKeyboardFocusEvent,
            new RoutedEventHandler((_, _) => SettleDown()));

        EventManager.RegisterClassHandler(typeof(UIElement), UIElement.LostKeyboardFocusEvent,
            new RoutedEventHandler((_, _) => SettleDown()));
    }

    /// <summary>
    /// The keys are up while a typing field has the caret, and not otherwise.
    ///
    /// Checked a moment later rather than on the instant. Moving between two boxes loses focus
    /// on the first before the second has it, and a dialog is mid-build when its first control
    /// takes the caret — acting on either would close the keyboard and reopen it in the same
    /// breath.
    /// </summary>
    /// <summary>
    /// Closes the keys when the press landed on something that is not typed into.
    ///
    /// The keyboard's own windows are exempt, obviously — pressing a key is not pressing
    /// somewhere else. So is anything inside a text box: a press there arrives here before the
    /// box's own handler opens the keyboard, and closing first would shut the keys on the very
    /// tap that asked for them.
    /// </summary>
    private static void PressedSomethingElse(object? window, object? pressed)
    {
        if (!IsOpen) return;
        if (ReferenceEquals(window, _keys) || ReferenceEquals(window, _button)) return;
        if (WithinSomethingTypedInto(pressed)) return;

        Close();
    }

    /// <summary>
    /// Whether a press landed inside a box the shop types into. Walks up, because a press on a
    /// TextBox arrives as the little inner part that draws the text, not as the box itself.
    /// </summary>
    private static bool WithinSomethingTypedInto(object? pressed)
    {
        for (var node = pressed as DependencyObject; node is not null;
             node = System.Windows.Media.VisualTreeHelper.GetParent(node)
                    ?? LogicalTreeHelper.GetParent(node))
        {
            if (TypedInto(node)) return true;
        }

        return false;
    }

    private static void SettleDown()
    {
        // Every focus change in the app comes through here, and almost none of them are about
        // the keyboard. Nothing to settle unless something is currently up.
        if (!IsOpen && _button is not { Awake: true }) return;

        _button?.Dispatcher.BeginInvoke(() =>
        {
            if (TypedInto(Keyboard.FocusedElement)) return;

            // Except while a key is under a finger. Pressing one is not supposed to move focus
            // at all — that is what the non-activating window buys — but if it ever does, the
            // keyboard closing under the shop's hand mid-word is the worst way to find out.
            if (DateTime.UtcNow - _lastKeyPress < TimeSpan.FromMilliseconds(600)) return;

            _button?.Sleep();
            Close();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private static DateTime _lastKeyPress = DateTime.MinValue;

    /// <summary>Called by the keyboard as a key goes down, so focus checks know to leave it alone.</summary>
    public static void NoteAKeyPress() => _lastKeyPress = DateTime.UtcNow;

    /// <summary>
    /// True when this app has a window on screen that somebody could be typing into — the
    /// keyboard's own two do not count, and neither does a minimised one.
    /// </summary>
    private static bool SomethingToTypeInto() =>
        Application.Current?.Windows.OfType<Window>().Any(
            w => w is not KeyboardWindow and not KeyboardButton
              && w.IsVisible
              && w.WindowState != WindowState.Minimized) == true;

    /// <summary>
    /// Takes the floating windows away while the shop is in another program.
    ///
    /// They are topmost, which is what puts them over this app's own dialogs and would just as
    /// happily put them over somebody's browser. The app never deactivates from its own
    /// keyboard being pressed — that is what makes these windows non-activating — so this only
    /// ever fires when the shop has genuinely gone somewhere else.
    /// </summary>
    private static void HideWhileAnotherAppIsInFront()
    {
        if (Application.Current is not { } app) return;

        app.Deactivated += (_, _) =>
        {
            _keys?.Hide();
            _button?.Hide();
        };

        app.Activated += (_, _) =>
        {
            // Only when there is actually a window of this app on screen to type into. The app
            // counts as activated in states where nothing of it is visible — everything
            // minimised, or a window closing — and a keyboard button floating over somebody's
            // desktop with no till behind it is a button belonging to nothing.
            if (_started && SomethingToTypeInto()) _button?.Show();
        };

        // And gone when the app goes, however it goes. Hung off Exit as well as the till's own
        // Closed, because a floating window that outlives its application is a window nothing
        // is left to close.
        app.Exit += (_, _) => Stop();
    }
}
