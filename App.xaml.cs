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

        // Answers "where is the shop?" from a command line, and writes it down. The same search
        // the Find the shop button runs, for a machine where somebody is trying to work out why
        // the till cannot see the server.
        if (e.Args.Contains("--find"))
        {
            // On a worker thread, waited on from here: this is the one place in the app that
            // blocks the thread the search would otherwise want to come back to.
            var found = Task.Run(() => Services.ShopFinder.Look()).GetAwaiter().GetResult();
            var said = found is null
                ? "No shop server answered on this machine's network."
                : $"Found {found.ShopName} at {found.Address}";

            Console.WriteLine(said);
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "MarketPos", "find.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {said}{Environment.NewLine}");
            }
            catch { /* the answer is on screen either way */ }

            Headless(found is null ? 1 : 0);
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

        CurrentJob = WhatThisOneIs(e.Args);

        // The till machine does not serve anybody. Leaving the server running on it would put a
        // second shop on the network, listening on the same port, answering with a catalogue
        // that is only ever a copy — and whichever machine a till found first would be the one
        // it believed.
        if (CurrentJob != Job.Till) ShopServer.Start();

        // ---------------------------------------------------------------- what to put on screen
        //
        // A server with a screen and a keyboard is the owner's machine: it holds the books, so
        // it is where the back office is. A server that is a box in the back has nothing to
        // show, and a window nobody looks at is a window somebody eventually closes — which on
        // that machine takes the shop's server down with it.
        if (e.Args.Contains("--server") || e.Args.Contains("--headless"))
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
        if (CurrentJob == Job.Server)
        {
            // The books, without the counter. Nobody sells on this machine, so there is no till
            // window to come back to and the back office is the whole of what it shows —
            // closed, and the machine has finished. The server itself goes on either way.
            if (!Views.StaffSignInWindow.Ask(null))
            {
                Shutdown();
                return;
            }

            var office = new Views.AdminWindow();
            MainWindow = office;
            office.Show();
            return;
        }

        MainWindow = new Views.MainWindow();
        MainWindow.Show();
    }

    /// <summary>Which of the shop's two machines this copy is running on.</summary>
    public enum Job { Shop, Server, Till }

    /// <summary>
    /// What this copy is, decided once at start-up from the file it was started as. A till on
    /// the counter finds this out so it can insist on belonging to a shop instead of quietly
    /// becoming one of its own.
    /// </summary>
    public static Job CurrentJob { get; private set; }

    /// <summary>
    /// Which of the shop's two machines this copy is running on.
    ///
    /// Taken from the name of the file that was started, so that a shop is handed two downloads
    /// and puts one on each machine, rather than the same one twice with a flag typed into a
    /// shortcut that the next person to set the machine up will not know about. The flags still
    /// work, and win, for anyone driving it by hand.
    /// </summary>
    private static Job WhatThisOneIs(string[] args)
    {
#if TILL_BUILD
        // The cashier's download. Nothing can talk it out of this — not a flag, and above all
        // not what the file happens to be called by the time it reaches the counter.
        return Job.Till;
#else
        if (args.Contains("--till")) return Job.Till;
        if (args.Contains("--server") || args.Contains("--headless")) return Job.Server;

        var name = System.IO.Path.GetFileNameWithoutExtension(
            Environment.ProcessPath ?? string.Empty);

        if (name.EndsWith("Server", StringComparison.OrdinalIgnoreCase)) return Job.Server;
        if (name.EndsWith("Till", StringComparison.OrdinalIgnoreCase)) return Job.Till;

        // The one download that is both, which is what a shop with one computer wants.
        return Job.Shop;
#endif
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
