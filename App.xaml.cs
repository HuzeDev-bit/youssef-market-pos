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

        // A new install opens with a password on the back office rather than without one.
        // Once, and only on an install that has never had one — see AdminAccount.
        AdminAccount.StartWithTheDefault();

        // Start the back-office server so both backend and frontend run together
        // from this single executable.
        ShopServer.Start();

        // ---------------------------------------------------------------- what to put on screen
        //
        // A shop with two computers can give the server one a screen and a keyboard, and then
        // it is an ordinary machine running the app that happens to answer the tills. Or it can
        // be a box in the back with nothing plugged into it, and then a window is something
        // nobody will ever look at and somebody will eventually close by accident — which takes
        // the shop's server down with it. --server is that second machine: the same program,
        // serving the tills, with nothing on screen to close.
        if (e.Args.Contains("--server"))
        {
            // Nothing will ever open a window, and an application whose last window closed is
            // an application that quits — so this one is told to keep going until it is
            // stopped from outside.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            return;
        }

        // Opened here rather than through StartupUri, which WPF acts on after this method
        // returns whatever has happened inside it — so every mode above had to end the process
        // outright to stop a till window being built behind it.
        MainWindow = new Views.MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShopServer.Stop();
        base.OnExit(e);
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
