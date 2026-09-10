using System.Windows;
using System.Windows.Input;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// The first thing a till asks on a machine with no shop configured yet.
///
/// A till that starts with no server address is a second, hidden shop — it sells from its
/// own little database and the owner's books never see a sale. Asking "which machine holds
/// the shop's database?" is the cheapest way to stop that; the answer is typed or found on
/// the network, and without one the till stays visibly unconnected instead of silently
/// inventing one of its own.
/// </summary>
public partial class ServerSetupWindow : Window
{
    private bool _workingAlone;

    public ServerSetupWindow()
    {
        InitializeComponent();
        Services.Localizer.Apply(this);
        Services.Responsive.Fit(this);

        AddressBox.Text = AppSettings.Current.ServerAddress;
        SetupHint.Text = Loc.T("An address looks like 192.168.1.20. The app fills in the rest.");

        Loaded += (_, _) => AddressBox.Focus();
    }

    /// <summary>
    /// Shows the dialog. True when a shop server was connected to; <paramref name="workingAlone"/>
    /// is true when the shop chose one computer deliberately instead.
    /// </summary>
    public static bool Ask(Window owner, out bool workingAlone)
    {
        var window = new ServerSetupWindow { Owner = owner };
        var connected = window.ShowDialog() == true;
        workingAlone = window._workingAlone;
        return connected;
    }

    /// <summary>
    /// Looks for the shop's server on this network and fills the box in with what it finds.
    /// </summary>
    private async void Find_Click(object sender, RoutedEventArgs e)
    {
        FindButton.IsEnabled = false;
        SetupHint.Text = Loc.T("Looking for the shop on this network…");

        try
        {
            var shop = await ShopFinder.Look();

            if (shop is null)
            {
                SetupHint.Text = Loc.T("No shop server answered. Check it is switched on and "
                                      + "that both machines are on the same network.");
                return;
            }

            AddressBox.Text = shop.Address;
            SetupHint.Text = Loc.T("Found {0} at {1}. Press Connect.",
                                   shop.ShopName, Loc.Ltr(shop.Address));
        }
        finally
        {
            FindButton.IsEnabled = true;
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var address = Address(AddressBox.Text);
        if (address.Length == 0)
        {
            SetupHint.Text = Loc.T("Type the shop's address, or press Find the shop.");
            return;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var parsed)
            || string.IsNullOrEmpty(parsed.Host))
        {
            SetupHint.Text = Loc.T("That does not look like an address. Try 192.168.1.20 "
                                  + "— or press Find the shop.");
            return;
        }

        AppSettings.Current.ServerAddress = address;
        AppSettings.Current.Save();

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// The one-computer case, chosen deliberately rather than fallen into. The setting stays
    /// empty, and the till keeps its own database — and the corner chip keeps saying so.
    /// </summary>
    private void Alone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _workingAlone = true;
        DialogResult = false;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Esc cancels, Enter connects.</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
    }

    /// <summary>Turns what was typed into an address the till can actually call.</summary>
    private static string Address(string? typed)
    {
        var text = (typed ?? string.Empty).Trim().TrimEnd('/');
        if (text.Length == 0) return string.Empty;

        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;

        return Uri.TryCreate(text, UriKind.Absolute, out var address) && address.IsDefaultPort
            ? $"{address.Scheme}://{address.Host}:5000"
            : text;
    }
}