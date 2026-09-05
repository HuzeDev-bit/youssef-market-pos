using System.Windows;
using System.Windows.Input;

namespace MarketPos.Views;

/// <summary>
/// In-app confirmation prompt. Replaces MessageBox, which renders as plain Windows
/// chrome against this borderless design — and can end up behind the main window.
/// </summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string heading, string? body = null)
    {
        InitializeComponent();

        HeadingText.Text = heading;
        BodyText.Text = body ?? string.Empty;
        BodyText.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows the prompt modally; true when the user confirms.</summary>
    public static bool Ask(Window owner, string heading, string? body = null) =>
        new ConfirmWindow(heading, body) { Owner = owner }.ShowDialog() == true;

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                No_Click(sender, e);
                break;
            case Key.Enter:
                Yes_Click(sender, e);
                break;
        }
    }
}
