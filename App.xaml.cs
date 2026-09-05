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

        if (e.Args.Contains("--linktest"))
        {
            var at = Array.IndexOf(e.Args, "--linktest") + 1;
            Headless(Services.LinkTest.Run(
                at < e.Args.Length ? e.Args[at] : "http://localhost:5000"));
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

        // Start the back-office server so both backend and frontend run together
        // from this single executable.
        ShopServer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShopServer.Stop();
        base.OnExit(e);
    }

    /// <summary>
    /// Ends a run that has no window to show.
    ///
    /// Shutdown alone is not enough: StartupUri is declared in App.xaml, and WPF goes on to
    /// build that window after OnStartup returns — so a headless mode that skipped
    /// Catalog.Load would raise the till anyway, watch its view model throw for want of a
    /// catalogue, and sit on an error dialog forever instead of exiting.
    /// </summary>
    private void Headless(int code)
    {
        // Leaves now, rather than through Shutdown. StartupUri is declared in App.xaml, so
        // WPF builds the till window once OnStartup returns no matter what Shutdown has been
        // asked for — and a mode that skipped Catalog.Load then watches the view model throw
        // for want of a catalogue and sits on an error dialog forever. StartupUri cannot be
        // cleared either; its setter refuses null. Ending the process is what actually stops
        // the window being built.
        Console.Out.Flush();
        Environment.Exit(code);
    }
}
