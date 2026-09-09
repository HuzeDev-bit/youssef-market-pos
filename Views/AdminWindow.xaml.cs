using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;
using MarketPos.ViewModels;
using MarketPos.Views.Admin;

namespace MarketPos.Views;

/// <summary>
/// The back-office shell: sidebar, title, date filter and one page at a time.
///
/// Pages are built on first visit and then kept, so flicking between Dashboard and Products
/// is instant on the kind of machine this runs on. Each page refreshes itself when the shared
/// date range changes rather than being rebuilt.
/// </summary>
public partial class AdminWindow : Window
{
    private readonly Dictionary<AdminPage, AdminPageBase> _pages = new();
    private AdminPage _current = AdminPage.Dashboard;

    public AdminShellViewModel Vm { get; } = new();

    public AdminWindow()
    {
        InitializeComponent();

        // Translated here as well as on load. Waiting for an event is what left the sidebar in
        // English while every other part of this same window was Arabic.
        Services.Localizer.Apply(this);

        // The whole back office scales to the screen it is on: sidebar, header, date filter
        // and page together.
        //
        // The two numbers are the shell's own furniture plus the room a page is promised.
        // Width: 222 of sidebar, 36 of window margin and 36 of page margin around the 1010
        // the widest page - the suppliers table - needs before its figures collapse into
        // ellipses, with a little over for the scrollbar. Height: 202 of logo, title, window
        // buttons and date chips above the 560 a page is given.
        //
        // Set either any smaller and PageHost's own minimums start a scrollbar inside a
        // window that has just been scaled precisely so that nothing needs to scroll.
        Services.Responsive.Shell(this, 1340, 770);
        DataContext = Vm;

        // Opens filling the screen, because that is what a back office is for — and can now
        // be pulled off it again. See Chrome for why this is not WindowState.Maximized.
        Chrome.Fill(this);
        ShowWindowSize();

        // The navigation has to fit the machine it is on, not the one it was drawn on.
        // Hung off the scroller rather than the window, because the scroller is the thing
        // that knows how much room there actually is — and it is told, whether the window
        // was resized, laid out for the first time, or measured by the screenshot harness.
        NavScroller.SizeChanged += (_, _) => FitTheNavigation();

        Vm.Dates.RangeChanged += (_, _) =>
        {
            UpdateSubtitle();
            Current?.OnRangeChanged();
        };

        Loaded += (_, _) =>
        {
            ShowWhatThisPersonMaySee();
            ShowPasswordState();
        };
    }

    private AdminPageBase? Current => _pages.GetValueOrDefault(_current);

    /// <summary>
    /// What each page is for. The sidebar only offers the ones the signed-in person holds,
    /// which is why a worker with nothing but <see cref="Permission.AddProductAtTill"/> opens
    /// this window onto a single entry instead of a wall of pages that would all refuse them.
    ///
    /// This hides buttons; it does not enforce anything. The pages themselves call
    /// <see cref="Session.Require"/>, so a page reached by any other route still fails closed.
    /// </summary>
    private static Permission Needs(AdminPage page) => page switch
    {
        AdminPage.AddProduct    => Permission.AddProductAtTill,
        AdminPage.Dashboard     => Permission.SeeFinancials,
        AdminPage.SalesHistory  => Permission.SeeAllSales,
        AdminPage.Categories    => Permission.ManageCategories,
        AdminPage.Inventory     => Permission.ManageInventory,
        AdminPage.Suppliers     => Permission.ManageSuppliers,
        AdminPage.Workers       => Permission.ManageWorkers,
        AdminPage.Expenses      => Permission.ManageExpenses,
        AdminPage.Reports       => Permission.SeeReports,
        AdminPage.Activity      => Permission.SeeActivityLog,
        _                       => Permission.SeeFinancials,
    };

    /// <summary>
    /// Trims the sidebar to what the signed-in person may open, then lands on the first of
    /// them. A group heading with nothing left under it goes too — an empty "MONEY" label
    /// tells a stock worker only that there is something they are missing.
    /// </summary>
    internal void ShowWhatThisPersonMaySee()
    {
        AdminPage? first = null;
        TextBlock? heading = null;
        var headingHasSomething = false;

        foreach (var child in NavList.Children.OfType<FrameworkElement>())
        {
            switch (child)
            {
                case TextBlock label:
                    if (heading is not null) heading.Visibility = Visible(headingHasSomething);
                    heading = label;
                    headingHasSomething = false;
                    break;

                case RadioButton { Tag: string tag } button
                    when Enum.TryParse<AdminPage>(tag, out var page):
                    var allowed = Session.Can(Needs(page));
                    button.Visibility = Visible(allowed);
                    if (!allowed) break;

                    headingHasSomething = true;
                    first ??= page;
                    break;
            }
        }

        if (heading is not null) heading.Visibility = Visible(headingHasSomething);

        if (first is null)
        {
            // Signed in, but holding nothing this window can show. Say so rather than
            // opening on a page that will only refuse them.
            PageTitle.Text = Loc.T("Nothing here for you");
            PageSubtitle.Text = $"{Session.CurrentName} has no back-office access. "
                              + "The owner sets this under Workers.";
            return;
        }

        GoTo(first.Value);
    }

    private static Visibility Visible(bool yes) => yes ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Jumps to a page from somewhere else — an alert linking to Inventory, say.</summary>
    public void GoTo(AdminPage page)
    {
        foreach (var button in Nav.FindAll(NavList))
        {
            if (button.Tag as string != page.ToString()) continue;
            button.IsChecked = true;   // raises Checked, which normally calls Show
            break;
        }

        // Checked stands down before the window has loaded, so a jump made during start-up
        // would light the rail and leave the old page underneath it. Showing here covers that
        // and costs nothing once loaded, when the page asked for is already the one on screen.
        if (_current != page || PageHost.Content is null) Show(page);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<AdminPage>(tag, out var page))
            Show(page);
    }

    private void Show(AdminPage page)
    {
        if (!_pages.TryGetValue(page, out var view))
        {
            view = Build(page);
            view.Attach(Vm.Dates, this);
            _pages[page] = view;
        }

        _current = page;
        PageHost.Content = view;
        PageTitle.Text = Loc.T(view.Title);
        DateBar.Visibility = view.UsesDateRange ? Visibility.Visible : Visibility.Collapsed;

        UpdateSubtitle();
        view.Refresh();
        Vm.RefreshAlerts();
    }

    private void UpdateSubtitle()
    {
        var view = Current;
        if (view is null) return;

        // Both halves translated apart: the subtitle is the page's own sentence and the range
        // is one of the chips above it, and they are written in different files.
        PageSubtitle.Text = view.UsesDateRange
            ? $"{Loc.T(view.Subtitle)} · {Loc.T(Vm.Dates.RangeLabel)}"
            : Loc.T(view.Subtitle);
    }

    private static AdminPageBase Build(AdminPage page) => page switch
    {
        AdminPage.AddProduct => new AddProductPage(),
        AdminPage.Dashboard => new DashboardPage(),
        AdminPage.SalesHistory => new SalesHistoryPage(),
        AdminPage.Categories => new CategoriesPage(),
        AdminPage.Inventory => new InventoryPage(),
        AdminPage.Suppliers => new SuppliersPage(),
        AdminPage.Workers => new WorkersPage(),
        AdminPage.Expenses => new ExpensesPage(),
        AdminPage.Reports => new ReportsPage(),
        AdminPage.Activity => new ActivityPage(),
        _ => new DashboardPage(),
    };

    private void Preset_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse<DatePreset>(tag, out var preset))
            Vm.Dates.Use(preset);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Catalog.Reload();
        Current?.Refresh();
        Vm.RefreshAlerts();
    }

    /// <summary>The middle window control: fill the screen, or come back off it.</summary>
    private void Size_Click(object sender, RoutedEventArgs e)
    {
        Chrome.Toggle(this);
        ShowWindowSize();
    }

    /// <summary>
    /// The button says what pressing it will do, which means it changes with the window.
    /// A control whose icon never moves reads as one that does not work.
    /// </summary>
    private void ShowWindowSize()
    {
        var filled = Chrome.FillsTheScreen(this);

        SizeGlyph.Data = (System.Windows.Media.Geometry)FindResource(
            filled ? "Icon.Restore" : "Icon.Maximize");
        SizeButton.ToolTip = Loc.T(filled ? "Make the window smaller" : "Fill the screen");
    }

    /// <summary>
    /// Tightens the navigation until it fits, before letting it scroll.
    ///
    /// Fifteen rows at their drawn height need about 570px. A 1366x768 laptop at the 125%
    /// scaling those machines ship with gives the sidebar around 460 — so the shop's own
    /// screen showed everything down to Staff and simply stopped, and the owner had no
    /// reason to think Reports existed. Scrolling is the backstop; fitting is the fix,
    /// because a menu you have to scroll is a menu you have to already know the shape of.
    ///
    /// Only the row height and the gaps around the group headings change. Nothing moves,
    /// nothing is hidden, and the type stays the size it was: a shorter row is still a row.
    /// </summary>
    private void FitTheNavigation()
    {
        if (NavList is null) return;

        var rows = NavList.Children.OfType<RadioButton>().ToList();
        var headings = NavList.Children.OfType<TextBlock>().ToList();
        if (rows.Count == 0) return;

        // What the sidebar actually has, asked of the thing that holds it.
        var available = NavScroller.ActualHeight;
        if (available <= 0) return;

        // The drawn size first, then tighter, then tighter again. Below that it scrolls: a
        // 24px row is a colour swatch rather than something to press.
        foreach (var (height, gap) in new[] { (40.0, 6.0), (34.0, 4.0), (30.0, 2.0) })
        {
            foreach (var row in rows) row.Height = height;
            foreach (var heading in headings)
                heading.Margin = new Thickness(12, gap, 0, gap);

            var needed = rows.Count * (height + 2) + headings.Count * (16 + gap * 2);
            if (needed <= available) return;
        }
    }

    private void Minimise_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void BackToTill_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// The language, and nothing else. The same screen the till opens.
    ///
    /// It used to be gated behind <c>ManageSettings</c>, which is the owner's alone, back when
    /// this window set the shop's name, currency and printer. Those have gone; what is left is
    /// which language the app speaks, and a manager who reads French being unable to change
    /// that — while a cashier can, from the till — would be a rule with nothing behind it.
    /// </summary>
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Rebuilt because the pages were built in the old language. It costs a reload of the
        // page on screen and nothing else; the shop's figures are not touched.
        if (SettingsWindow.Ask(this)) Rebuild();
    }

    /// <summary>Throws away every built page, so a settings change reaches all of them.</summary>
    private void Rebuild()
    {
        var current = _current;
        _pages.Clear();
        PageHost.Content = null;
        Show(current);
    }

    /// <summary>
    /// The owner's own password.
    ///
    /// It lives here, next to their name, because there was nowhere else: staff passwords are
    /// set on the Workers page, but the owner is not a worker — they have no row to click.
    /// Without it the back office opens for anyone who presses the lock, which is a fair
    /// default for a shop that has not chosen one and a bad one for a shop that has staff.
    /// </summary>
    private void Password_Click(object sender, RoutedEventArgs e)
    {
        if (!Session.IsOwnerUnlocked)
        {
            ConfirmWindow.Ask(this, "Not your password to set",
                $"{Session.CurrentName} signed in as staff. Only the owner can change the "
                + "owner's password.");
            return;
        }

        if (AdminLoginWindow.Ask(this, changePassword: true)) ShowPasswordState();
    }

    /// <summary>
    /// Says whether the office is actually locked. A shop with no owner password is open to
    /// whoever is standing at the machine, and that should be visible rather than assumed.
    /// </summary>
    private void ShowPasswordState()
    {
        PasswordButton.ToolTip = AdminAccount.IsConfigured
            ? "Change your password"
            : "No password set — anyone can open the back office";

        PasswordButton.Foreground = (System.Windows.Media.Brush)FindResource(
            AdminAccount.IsConfigured ? "Brush.Muted" : "Brush.Accent");
    }

    /// <summary>
    /// Hands the machine back. Closing this window on its own keeps the sign-in — which is
    /// what you want when the owner steps out to the till mid-job — so there has to be a way
    /// to say "I am done", or the next person to touch the lock walks in as whoever was here
    /// last.
    /// </summary>
    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, $"Sign {Session.CurrentName} out?",
                "The back office will ask for a name and password again.")) return;

        Session.SignOut();
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.F5) Refresh_Click(sender, e);
    }

    /// <summary>Walks the sidebar for the nav buttons, which are nested inside group headers.</summary>
    private static class Nav
    {
        public static IEnumerable<RadioButton> FindAll(DependencyObject root)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is RadioButton button) yield return button;
                foreach (var nested in FindAll(child)) yield return nested;
            }
        }
    }
}

/// <summary>Shell-level state: who is signed in, the shared date range, and the alert count.</summary>
public sealed class AdminShellViewModel : ViewModelBase
{
    private int _alertCount;

    public AdminContext Dates { get; } = new();

    public string BusinessName => AppSettings.Current.BusinessName;
    public string UserName => Session.CurrentName;
    public string UserRole => Loc.T(Session.CurrentRole switch
    {
        WorkerRole.Owner => "Owner",
        WorkerRole.Manager => "Manager",
        WorkerRole.StockWorker => "Stock worker",
        _ => "Cashier",
    });

    public int AlertCount
    {
        get => _alertCount;
        private set { if (SetField(ref _alertCount, value)) OnPropertyChanged(nameof(HasAlerts)); }
    }

    public bool HasAlerts => AlertCount > 0;

    public void RefreshAlerts() => AlertCount = Notifications.Build().Count;
}
