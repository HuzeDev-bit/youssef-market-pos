using System.Windows;
using System.Windows.Media.Animation;
using MarketPos.Services;

namespace MarketPos.Views;

public partial class KeyboardButton : Window
{
    /// <summary>Room left between the button and the corner of the screen.</summary>
    private const double Inset = 14;

    public KeyboardButton()
    {
        InitializeComponent();
        Loaded += (_, _) => ((Storyboard)FindResource("Arrive")).Begin();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Floating.KeepFocusElsewhere(this);
        Place();
    }

    /// <summary>
    /// True while something on screen is waiting to be typed into. Drives the colour, and is a
    /// dependency property so the button's own template can follow it without any code.
    /// </summary>
    public bool Awake
    {
        get => (bool)GetValue(AwakeProperty);
        set => SetValue(AwakeProperty, value);
    }

    public static readonly DependencyProperty AwakeProperty =
        DependencyProperty.Register(nameof(Awake), typeof(bool), typeof(KeyboardButton),
                                    new PropertyMetadata(false));

    /// <summary>
    /// Bottom left, which is the emptiest corner of both screens: the till keeps its icon rail
    /// and the back office its sidebar on the other side, and a dialog opens in the middle.
    /// </summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Inset;
        Top = area.Bottom - Height - Inset;
    }

    /// <summary>
    /// Wakes the button up: the shop has just tapped into something it can type in.
    ///
    /// The ring is thrown once and then it settles. Repeating it would turn the one moment
    /// worth noticing into wallpaper.
    /// </summary>
    public void Wake()
    {
        Place();
        Awake = true;
        ((Storyboard)FindResource("Wake")).Begin();
    }

    public void Sleep() => Awake = false;

    private void Tap_Click(object sender, RoutedEventArgs e) => TouchKeyboard.Toggle();
}
