using System.Windows;
using System.Windows.Controls;
using MarketPos.ViewModels;

namespace MarketPos.Views.Admin;

/// <summary>
/// Shared behaviour for every back-office page.
///
/// A page declares its own title and whether the date filter applies to it, then implements
/// <see cref="Load"/>. The shell handles when to call it: on first show, on a manual reload,
/// and whenever the owner changes the period.
/// </summary>
public abstract class AdminPageBase : UserControl
{
    protected AdminContext Dates { get; private set; } = new();
    protected AdminWindow? Shell { get; private set; }

    public abstract string Title { get; }
    public virtual string Subtitle => string.Empty;

    /// <summary>False for pages whose content does not depend on a period — Products, Settings.</summary>
    public virtual bool UsesDateRange => false;

    public void Attach(AdminContext dates, AdminWindow shell)
    {
        Dates = dates;
        Shell = shell;
    }

    public void Refresh()
    {
        try
        {
            Load();

            // A page writes most of its own words here — summaries, empty states, the note
            // under a figure — long after the class handler that translates labels has fired.
            // Re-walking the page is what makes those speak the shop's language too, and it
            // costs a dictionary lookup per label on a screen that has just hit the database.
            Services.Localizer.Apply(this);
        }
        catch (UnauthorizedAccessException error)
        {
            // A permission failure is an expected outcome, not a crash: the page simply says
            // what is not allowed rather than taking the window down.
            ShowBlocked(error.Message);
        }
    }

    public virtual void OnRangeChanged()
    {
        if (UsesDateRange) Refresh();
    }

    protected abstract void Load();

    /// <summary>Replaces the page body with an explanation when the signed-in role may not see it.</summary>
    protected virtual void ShowBlocked(string message)
    {
        Content = new Border
        {
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Not available to you",
                        Style = (Style)FindResource("Text.EmptyTitle"),
                    },
                    new TextBlock
                    {
                        Text = message,
                        Style = (Style)FindResource("Text.EmptyBody"),
                    },
                },
            },
        };
    }

    /// <summary>Reloads and tells the shell to recount alerts — used after any write.</summary>
    protected void ReloadAll()
    {
        Refresh();
        Shell?.Vm.RefreshAlerts();
    }

    protected bool Confirm(string heading, string? body = null) =>
        Shell is not null && ConfirmWindow.Ask(Shell, heading, body);
}
