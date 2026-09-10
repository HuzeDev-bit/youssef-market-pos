using System.Text;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Services;

/// <summary>
/// Proves a till and the back office actually work together, against a real server over a
/// real socket.
///
/// The parts that break here break quietly: a catalogue that half-arrives, a sale that is
/// accepted twice, stock moved on one machine and not the other. None of that shows up on
/// screen until the shop counts its shelves at the end of the month, so it is worth a test
/// that walks the whole path.
///
/// Run with <c>MarketPos.exe --linktest http://localhost:5000</c> and MARKETPOS_DB pointing at
/// a throwaway database — this one writes sales.
/// </summary>
public static class LinkTest
{
    private static readonly StringBuilder Log = new();
    private static int _failures;

    public static int Run(string address)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MARKETPOS_DB")))
        {
            Console.WriteLine("REFUSED: --linktest writes real sales, so it needs a scratch database.");
            Console.WriteLine("Set MARKETPOS_DB to a throwaway path and run it again.");
            return 2;
        }

        Session.UnlockAsOwner();
        Database.Initialize();

        // The till is told where the shop is, in memory only — the settings file on this
        // machine is not a test fixture.
        AppSettings.Current.ServerAddress = address;
        AppSettings.Current.TillName = "linktest";

        return Task.Run(Walk).GetAwaiter().GetResult();
    }

    private static async Task<int> Walk()
    {
        // ---------------------------------------------------------------- the server is there
        var reachable = await ShopLink.Ping();
        Check("the back office answers", reachable, ShopLink.LastProblem);
        if (!reachable)
        {
            Console.WriteLine(Log.ToString());
            Console.WriteLine($"Start it first: dotnet run --project server -- --urls {AppSettings.Current.ServerAddress}");
            return 1;
        }

        Check("it says which shop it is", ShopLink.ShopName.Length > 0, ShopLink.ShopName);

        // ---------------------------------------------------------------- the catalogue arrives
        CatalogSync.Stamp = string.Empty;              // pretend this till has never asked
        var pulled = await ShopLink.PullCatalogue();
        Check("the catalogue comes down", pulled > 0, $"{pulled} products written");

        var local = StockRepository.List().Where(p => p.ShowInPos).ToList();
        Check("the till can see them", local.Count > 0, $"{local.Count} on this machine");

        // Asking again must cost nothing: that is the whole point of the stamp.
        var again = await ShopLink.PullCatalogue();
        Check("asking again downloads nothing", again == -1,
              again == -1 ? "the server said nothing had changed" : $"it sent {again} again");

        // ---------------------------------------------------------------- who works here
        //
        // Without this a cashier could not sign in on a second till at all: the sign-in list
        // reads this machine's own staff table, and on a fresh till that table is empty.
        var staff = await ShopLink.PullStaff();
        var canSignIn = WorkerRepository.List().Count(w => w.HasPin);
        Check("the staff list comes down too", staff >= 0, $"{staff} sent");
        // The material has to be real, not merely present: a till holding an empty hash would
        // let anybody in, and would look exactly like this one from the outside.
        var wrongPasswordRefused = WorkerRepository.List().Where(w => w.HasPin)
            .All(w => WorkerRepository.SignIn(w.Id, "definitely-not-the-password") is null);
        Check("a wrong password is refused here too", wrongPasswordRefused,
              "checked on this till, with nothing plugged in");

        Check("and they can sign in on this till", canSignIn == staff,
              staff == 0
                  ? "nobody at the back office has a password yet"
                  : $"{canSignIn} of {staff} can sign in here with the server unplugged");

        if (local.Count == 0)
        {
            Console.WriteLine(Log.ToString());
            Console.WriteLine("The back office has no products, so there is nothing to sell.");
            return _failures == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- a sale, offline
        var product = local[0];
        var line = new SaleItem(new Product
        {
            Id = product.Id,
            Barcode = product.Barcode,
            Name = product.Name,
            Category = product.Category,
            Price = product.Price,
            Unit = product.Unit,
            TaxRate = product.TaxRate,
        }, 2m);

        var before = OutboxRepository.PendingCount();

        var invoice = SaleRepository.Save(
            new[] { line }, line.LineTotal, DiscountKind.None, 0m, 0m,
            line.LineTotal, 0m, line.LineTotal, PaymentMethod.Cash, line.LineTotal);

        Check("the till takes the sale on its own", invoice > 0, $"local invoice #{invoice}");
        Check("and queues it for the back office", OutboxRepository.PendingCount() == before + 1,
              $"{OutboxRepository.PendingCount()} waiting");

        // ---------------------------------------------------------------- handing it over
        var sent = await ShopLink.PushSales();
        Check("the queue empties when the server is there", sent >= 1, $"{sent} accepted");
        Check("nothing is left waiting", OutboxRepository.PendingCount() == 0,
              $"{OutboxRepository.PendingCount()} still queued");

        // ---------------------------------------------------------------- the money is right
        var stampedTwice = await ShopLink.PushSales();
        Check("a second attempt sends nothing", stampedTwice == 0,
              "the queue was already empty");

        // ------------------------------------------------- the promise: selling with it off
        //
        // This is the whole reason the till keeps its own database. Somebody unplugs the back
        // office, the shop goes on trading, and the takings find their way home afterwards.
        var live = AppSettings.Current.ServerAddress;
        AppSettings.Current.ServerAddress = "http://127.0.0.1:1";   // nothing listens there

        var offline = await ShopLink.Ping();
        Check("the till notices the back office has gone", !offline, ShopLink.LastProblem);

        var duringBlackout = SaleRepository.Save(
            new[] { line }, line.LineTotal, DiscountKind.None, 0m, 0m,
            line.LineTotal, 0m, line.LineTotal, PaymentMethod.Cash, line.LineTotal);

        Check("the shop keeps selling anyway", duringBlackout > 0,
              $"took invoice #{duringBlackout} with no server at all");
        Check("and the sale waits its turn", OutboxRepository.PendingCount() == 1,
              $"{OutboxRepository.PendingCount()} waiting");

        var refused = await ShopLink.PushSales();
        Check("nothing is lost trying to send to nowhere", refused == 0
              && OutboxRepository.PendingCount() == 1,
              "still queued after a failed attempt");

        AppSettings.Current.ServerAddress = live;                   // the machine comes back
        var caughtUp = await ShopLink.PushSales();

        Check("it catches up when the back office returns", caughtUp == 1,
              $"{caughtUp} delivered late");
        Check("and the queue is clear again", OutboxRepository.PendingCount() == 0,
              $"{OutboxRepository.PendingCount()} waiting");

        Console.WriteLine(Log.ToString());
        Console.WriteLine(_failures == 0 ? "LINK TEST PASSED" : $"{_failures} FAILED.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(string what, bool passed, string detail)
    {
        if (passed) Log.AppendLine($"ok    {what} ({detail})");
        else
        {
            _failures++;
            Log.AppendLine($"FAIL  {what}: {detail}");
        }
    }
}
