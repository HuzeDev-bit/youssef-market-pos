using System.Windows;
using MarketPos.Services;

namespace MarketPos;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface crashes instead of letting the window vanish silently.
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "MarketPos", "crash.log"),
                    args.Exception.ToString());
            }
            catch { /* logging must never mask the original fault */ }

            MessageBox.Show(args.Exception.Message, "Market POS error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Before anything else, including the headless modes. Every label is translated as it
        // loads and Arabic lays the whole interface out right to left — and the diagnostics
        // that photograph every screen have to go through the same path the shop does, or they
        // report on an app nobody will ever run.
        Loc.Load();
        Localizer.Start();

        if (e.Args.Contains("--flowtest"))
        {
            // Runs before Catalog.Load so the scratch database is not seeded with demo
            // products that would muddle the figures being asserted.
            Headless(Services.FlowTest.Run());
            return;
        }

        if (e.Args.Contains("--icons"))
        {
            var target = Array.IndexOf(e.Args, "--icons") + 1;
            Headless(Services.IconSheet.Write(this,
                target < e.Args.Length ? e.Args[target] : "icons.png"));
            return;
        }

        try
        {
            // Must run before the main window builds its view model — the product grid
            // binds straight to the catalogue.
            Catalog.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The till could not open its database.\n\n{ex.Message}\n\n{MarketPos.Data.Database.Path}",
                "Market POS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (e.Args.Contains("--selftest")) SelfTest.Run(this);

        // A new install opens with a password on the back office rather than without one.
        // Once, and only on an install that has never had one — see AdminAccount.
        AdminAccount.StartWithTheDefault();

        // One computer, one program. The shop's database is on the machine the app runs on,
        // and nothing here talks to a network.
        MainWindow = new Views.MainWindow();
        MainWindow.Show();
    }

    /// <summary>
    /// Ends a run that has no window to show, with the exit code the diagnostics reported.
    ///
    /// Leaves through the process rather than through Shutdown, which asks WPF to stop once it
    /// gets back to its message loop and is therefore not a way to stop what the rest of this
    /// method would do next.
    /// </summary>
    private void Headless(int code)
    {
        Console.Out.Flush();
        Environment.Exit(code);
    }
}
