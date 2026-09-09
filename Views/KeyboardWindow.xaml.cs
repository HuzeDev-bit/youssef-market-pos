using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// The keys themselves: built rather than drawn, because there are three layouts and the panel
/// is measured against the screen it lands on rather than the one it was written on.
/// </summary>
public partial class KeyboardWindow : Window
{
    private KeyboardLayout _layout = KeyboardLayout.ForTheShop();
    private bool _shift;

    /// <summary>Gap between keys.</summary>
    private const double Gap = 6;

    /// <summary>Padding inside the panel, and the height of the drag handle above the keys.</summary>
    private const double PadTop = 12, PadBottom = 14, PadSide = 14, GripHeight = 30;

    /// <summary>
    /// The widest row is twelve units across. Every row is measured against that number, so the
    /// columns line up down the panel instead of each row finding its own width.
    /// </summary>
    private const double Units = 12;

    /// <summary>
    /// Where the shop has put it, once the shop has put it anywhere. Null until then, and the
    /// keyboard docks itself along the bottom. Moving it is a decision, and re-docking it on
    /// the next open would throw that decision away every time.
    /// </summary>
    private Point? _placed;

    public KeyboardWindow()
    {
        InitializeComponent();

        var area = SystemParameters.WorkArea;
        Build(new Size(area.Width, area.Height));
        FollowTheScreen();

        // Said on the way down, which is when focus would move if it were going to. The keys
        // are not supposed to take it — that is what the non-activating window is for — but
        // the keyboard closing under the shop's hand mid-word is the worst way to find out
        // that some control somewhere disagrees.
        PreviewMouseDown += (_, _) => TouchKeyboard.NoteAKeyPress();
        PreviewTouchDown += (_, _) => TouchKeyboard.NoteAKeyPress();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The one thing that makes this a keyboard rather than a window with keys drawn on it.
        // Without it the first tap moves the caret out of whatever was being filled in.
        Floating.KeepFocusElsewhere(this);
    }

    // ============================== Coming and going ==============================

    /// <summary>Slides up from under the bottom of the screen.</summary>
    public void SlideIn()
    {
        if (IsVisible) return;

        Place();
        Show();

        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From = Height,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>
    /// Slides back down, and only hides once it has finished — hiding first would leave the
    /// animation playing to nobody and the panel would simply vanish.
    /// </summary>
    public void SlideOut()
    {
        if (!IsVisible) return;

        var down = new DoubleAnimation
        {
            To = Height,
            Duration = TimeSpan.FromMilliseconds(190),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };

        down.Completed += (_, _) => Hide();
        Slide.BeginAnimation(TranslateTransform.YProperty, down);
    }

    /// <summary>
    /// Sizes the panel to the screen and puts it along the bottom — or leaves it wherever the
    /// shop dragged it to, clamped back inside the screen in case that screen has since changed.
    ///
    /// The height is worked out rather than measured. Asking the layout how tall it wants to be
    /// before it has been arranged answers zero, and the panel then sized itself to a fallback
    /// and hung off the bottom of the screen with three rows of keys below the edge.
    /// </summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;

        FitTo(new Size(area.Width, area.Height));

        var left = _placed?.X ?? area.Left + (area.Width - Width) / 2;
        var top = _placed?.Y ?? area.Bottom - Height;

        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    /// <summary>
    /// Sizes the panel and its keys for a screen of this size, and answers what it came to.
    ///
    /// Takes the screen rather than reading it, so the diagnostics can stand the keyboard on
    /// screens this machine does not have. A keyboard is the one part of the app that must fit
    /// whatever it lands on — there is no scrolling a keyboard and no reading it later.
    /// </summary>
    public Size FitTo(Size screen)
    {
        Width = Roll(screen);
        Height = Build(screen);
        return new Size(Width, Height);
    }

    /// <summary>
    /// How wide the panel is: the shop's chosen size, and never wider than the screen.
    /// </summary>
    private double Roll(Size screen) => Math.Min(screen.Width, MaxRoll * Scale);

    /// <summary>
    /// How big the shop has asked the keys to be. Remembered between openings and between
    /// runs — a size chosen once with a fingertip is not a thing anybody wants to choose
    /// again every morning.
    /// </summary>
    private static double Scale
    {
        get => Math.Clamp(Services.AppSettings.Current.KeyboardScale ?? 1.0, 0.7, 1.4);
        set
        {
            Services.AppSettings.Current.KeyboardScale = Math.Clamp(value, 0.7, 1.4);
            Services.AppSettings.Current.Save();
        }
    }

    private void Smaller_Click(object sender, RoutedEventArgs e) => Resize(-0.1);
    private void Bigger_Click(object sender, RoutedEventArgs e) => Resize(+0.1);

    /// <summary>
    /// Grows or shrinks the keys, keeping the panel where it sits rather than where its top
    /// left corner happens to be — a keyboard that walked up the screen every time it was made
    /// bigger would have to be dragged back after every press.
    /// </summary>
    private void Resize(double by)
    {
        var was = Scale;
        Scale = was + by;
        if (Math.Abs(Scale - was) < 0.001) return;

        var area = SystemParameters.WorkArea;
        var bottom = Top + Height;
        var middle = Left + Width / 2;

        FitTo(new Size(area.Width, area.Height));

        Left = Math.Clamp(middle - Width / 2, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(bottom - Height, area.Top, Math.Max(area.Top, area.Bottom - Height));

        if (_placed is not null) _placed = new Point(Left, Top);
    }

    /// <summary>
    /// Never wider than this, whatever the screen. Stretched across a 1920px counter display a
    /// keyboard puts A and L a forearm apart; capped, the keys stay the size of a fingertip and
    /// the whole thing stays inside one reach.
    /// </summary>
    private const double MaxRoll = 1180;

    /// <summary>
    /// Follows the screen when the screen itself changes — a resolution change, a laptop
    /// undocked, a projector unplugged. The keys are sized from it, so a keyboard that did not
    /// follow would be left either off the bottom edge or half the width of the till.
    /// </summary>
    private void FollowTheScreen()
    {
        void Changed(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(() =>
            {
                // Whatever it was dragged to belonged to the old screen.
                _placed = null;
                if (IsVisible) Place();
            });

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += Changed;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= Changed;
    }

    // ============================== Moving it about ==============================

    private Point _grabbedAt;
    private bool _dragging;

    private void Grip_Down(object sender, MouseButtonEventArgs e)
    {
        // Not DragMove: that asks Windows to run its own move loop, which begins by activating
        // the window — and this window's whole job is never to be activated.
        _grabbedAt = e.GetPosition(this);
        _dragging = Grip.CaptureMouse();
    }

    private void Grip_Move(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        var now = PointToScreen(e.GetPosition(this));
        Left = now.X - _grabbedAt.X;
        Top = now.Y - _grabbedAt.Y;
    }

    private void Grip_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        Grip.ReleaseMouseCapture();

        // Remembered from here on, so the next time it comes up it comes up where it was left.
        _placed = new Point(Left, Top);
    }

    // ============================== Building the keys ==============================

    /// <summary>Rebuilds every row for the current layout, and answers how tall that made it.</summary>
    private double Build(Size screen)
    {
        var width = Roll(screen);

        var unit = Math.Clamp((width - PadSide * 2 - Gap * (Units - 1)) / Units, 30, 110);

        // Never more than this much of the screen. On a short screen a keyboard sized purely
        // from the width would take two thirds of it and bury the thing being typed into.
        var room = screen.Height * 0.55 - GripHeight - PadTop - PadBottom;
        var height = Math.Clamp(Math.Min(unit * 0.86, room / 5 - Gap), 26, 86);

        Rows.Children.Clear();
        Rows.Children.Add(DigitsRow(unit, height));

        foreach (var letters in _layout.Rows)
            Rows.Children.Add(LetterRow(letters, unit, height));

        Rows.Children.Add(CommandRow(unit, height, width - PadSide * 2));

        // Five rows, each carrying its own bottom gap, plus the handle and the padding.
        return Math.Round(5 * (Math.Round(height) + Gap) + GripHeight + PadTop + PadBottom + 2);
    }

    /// <summary>Rebuilds and resizes in place — a layout or shift change alters the height.</summary>
    private void Rebuild()
    {
        var area = SystemParameters.WorkArea;
        var height = Build(new Size(area.Width, area.Height));
        if (Math.Abs(Height - height) < 0.5) return;

        // Grown or shrunk from the top, so a keyboard sitting on the bottom edge stays on it.
        Top += Height - height;
        Height = height;
    }

    private StackPanel DigitsRow(double unit, double height)
    {
        var row = Row();

        foreach (var digit in KeyboardLayout.Digits)
            row.Children.Add(Key(digit.ToString(), unit, height, () => KeyStrokes.Type(digit.ToString())));

        row.Children.Add(Key("⌫", unit * 2, height, () => KeyStrokes.Press(KeyStrokes.Backspace),
                             "Key.Command"));
        return row;
    }

    private StackPanel LetterRow(string letters, double unit, double height)
    {
        var row = Row();

        foreach (var letter in letters)
        {
            var text = _shift && _layout.HasCase
                ? letter.ToString().ToUpperInvariant()
                : letter.ToString();

            row.Children.Add(Key(text, unit, height, () =>
            {
                KeyStrokes.Type(text);

                // Shift lets go after one letter, the way a phone does. Holding it down for a
                // whole word is a keyboard habit, and there is no key to hold here.
                if (_shift) { _shift = false; Rebuild(); }
            }));
        }

        return row;
    }

    /// <summary>Shift, the marks, the space bar, the layout switch, and the way out.</summary>
    /// <summary>Shift, the marks, the space bar, the layout switch, and the way out.</summary>
    ///
    /// <remarks>
    /// Laid out by weight and then made to fit, because this row is the one that does not
    /// naturally come to twelve units like the others. It was 14.8 of them in a panel twelve
    /// wide: the row is centred, so it hung off both ends and the keys at the edges were
    /// simply not on the screen. The first of those was the decimal point — the one key a till
    /// cannot do without.
    ///
    /// Normalising rather than hand-tuning the numbers, so the next key added here shrinks the
    /// row instead of pushing something off the end of it.
    /// </remarks>
    private StackPanel CommandRow(double unit, double height, double inner)
    {
        var keys = new List<(string Text, double Weight, string Style, Action Press)>();

        if (_layout.HasCase)
            keys.Add(("⇧", 1.4, _shift ? "Key.Chosen" : "Key.Command",
                      () => { _shift = !_shift; Rebuild(); }));

        foreach (var mark in KeyboardLayout.Marks)
        {
            var one = mark.ToString();
            keys.Add((one, 0.8, "Key.Command", () => KeyStrokes.Type(one)));
        }

        keys.Add((" ", 3.0, "Key.Command", () => KeyStrokes.Type(" ")));

        // Empties the box in one press. Holding backspace down to clear a mistyped barcode is
        // twelve presses and no way to be sure it is empty.
        keys.Add((Loc.T("Clear"), 1.5, "Key.Command", KeyStrokes.ClearField));

        // One key per language rather than a cycle: the shop knows which of the three it wants,
        // and a cycle would put Arabic two presses away from English half the time.
        foreach (var layout in KeyboardLayout.All)
        {
            var pick = layout;
            keys.Add((layout.Name, 1.0, layout.Code == _layout.Code ? "Key.Chosen" : "Key.Command",
                      () => { _layout = pick; _shift = false; Rebuild(); }));
        }

        keys.Add(("⏎", 1.4, "Key.Enter", () => KeyStrokes.Press(KeyStrokes.Enter)));
        keys.Add(("✕", 1.0, "Key.Command", TouchKeyboard.Close));

        // Made to fit the panel, gaps and all.
        //
        // Counting the gaps matters here and nowhere else: a unit is sized on the assumption
        // that a row holds twelve keys and so eleven gaps between them, and this row holds
        // thirteen. Normalising on the units alone left it two pixels over — which is enough
        // for a centred row to lose a sliver off each end.
        var wanted = keys.Sum(k => k.Weight);
        var room = inner - keys.Count * Gap;
        var fit = Math.Min(1.0, room / (unit * wanted));

        var row = Row();
        foreach (var (text, weight, style, press) in keys)
            row.Children.Add(Key(text, unit * weight * fit, height, press, style));

        return row;
    }

    private static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, Gap),

        // Left to right whatever language the app is in. A keyboard is a picture of a physical
        // object, and mirroring it in Arabic would put the layout switch where Backspace sits
        // on the machine under the screen.
        FlowDirection = FlowDirection.LeftToRight,
    };

    private Button Key(string text, double width, double height, Action press, string style = "Key")
    {
        var key = new Button
        {
            Content = text,
            // Floor, not round. A row is a budget of whole units; rounding each key up
            // spends a fraction of a pixel more than the row was given, twelve times over,
            // and the row ends up wider than the panel that was sized to hold it.
            Width = Math.Floor(width),
            Height = Math.Round(height),
            Margin = new Thickness(Gap / 2, 0, Gap / 2, 0),
            Style = (Style)FindResource(style),
        };

        key.Click += (_, _) => press();
        return key;
    }
}
