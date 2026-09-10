using System.Text;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Services;

/// <summary>
/// Proves a cashier's machine can do a day's work without a database of its own.
///
/// <para>
/// Run with <c>MarketPosTill.exe --tilltest http://the-shop:5000</c>. It puts the process into
/// the state a remote till is in — no database, and the door to one locked — and then walks
/// the things a cashier actually does: sign in, look at the categories, find a product, check
/// a price, scan something into the basket, pay, look at the tickets, reprint one, read the
/// shop's name off a receipt.
/// </para>
///
/// <para>
/// The proof is not that these succeed. It is that none of them reaches for a database: with
/// <see cref="Data.Database.NotOnThisMachine"/> set, the first thing that tried would throw and
/// the check it was part of would fail by name. A grep can tell you a call is guarded; this
/// tells you the guard held while the code actually ran.
/// </para>
/// </summary>
public static class TillTest
{
    private static readonly StringBuilder Log = new();
    private static int _failures;

    /// <summary>
    /// The password this exercise signs in with. A new install starts with it and this run is
    /// against a scratch shop, so it is the one the server will have.
    /// </summary>
    private static string OwnerPassword = AdminAccount.Starting;

    public static int Run(string address, string? ownerPassword = null)
    {
        if (!string.IsNullOrEmpty(ownerPassword)) OwnerPassword = ownerPassword;

        // A till, and nothing behind it.
        Catalog.BelongsToAServer = true;
        Data.Database.NotOnThisMachine = true;
        Catalog.NothingToSell();

        AppSettings.Current.ServerAddress = address.TrimEnd('/');

        Say($"pointed at {AppSettings.Current.ServerAddress}");
        Say($"database on this machine: forbidden ({Data.Database.NotOnThisMachine})");
        Say(string.Empty);

        if (!Ask("the shop answers at all", () => ShopLink.Now(() => ShopLink.Ask()) is not null))
        {
            Say(string.Empty);
            Say("the shop did not answer, so nothing below could be tried.");
            return Finish();
        }

        // ------------------------------------------------------------ what the shop sells
        var catalogue = ShopLink.Now(() => ShopLink.PullCatalogue());
        Ask("the catalogue arrives over the wire", () => catalogue >= 0);
        Ask("and lands in memory, not in a file", () => Catalog.HasTheShopsAnswer);
        Say($"      {Catalog.Products.Count} product(s), {Catalog.Categories.Count - 1} categor(y|ies)");

        // ------------------------------------------------------------ who is at the counter
        var staff = ShopLink.Now(() => ShopLink.Staff());
        Ask("the shop says who may sign in", () => staff is not null);

        // ------------------------------------------------------------ what is this worth
        var onTheShelf = Catalog.Products.FirstOrDefault(p => p.IsScannable);
        if (onTheShelf is not null)
        {
            var answer = PriceCheck.For(onTheShelf.Barcode);
            Ask("a price check answers from the shop",
                () => answer.Found && answer.Item?.Price == onTheShelf.Price);
            Say($"      {answer.Name} at {answer.PriceText}");

            var nothing = PriceCheck.For("6119999999999");
            Ask("and says plainly when the shop has no such thing",
                () => !nothing.Found && !nothing.Unreachable);
        }
        else
        {
            Say("      (no scannable product at the shop, so the scan checks were skipped)");
        }

        // ------------------------------------------------------------ a sale, start to finish
        var sellable = Catalog.Products.FirstOrDefault(p => p.Stock > 0m && p.SoldAtTheTill);
        if (sellable is not null)
        {
            var till = new ViewModels.SaleViewModel();
            till.SearchText = sellable.IsScannable ? sellable.Barcode : sellable.Name;
            till.SubmitBarcodeCommand.Execute(null);

            Ask("scanning puts it in the basket", () => till.Cart.Count == 1);

            var wasOnTheShelf = sellable.Stock;
            till.CompleteSale(PaymentMethod.Cash, till.Total);

            Ask("the shop banked the sale and gave a ticket number", () => till.LastInvoiceNumber > 0);
            Say($"      ticket #{till.LastInvoiceNumber}");

            // ------------------------------------------------------------ and the shelf moved
            ShopLink.Now(() => ShopLink.PullCatalogue());
            var after = Catalog.Products.FirstOrDefault(p => p.Id == sellable.Id);
            Ask("and the shop's own stock came down",
                () => after is not null && after.Stock < wasOnTheShelf);
            Say($"      {wasOnTheShelf} -> {after?.Stock}");

            // ------------------------------------------------------------ the paper
            var paper = Receipts.Find(till.LastInvoiceNumber);
            Ask("the ticket comes back for printing", () => paper is not null);
            Ask("with the lines that were sold", () => paper?.Lines.Count == 1);

            var again = Receipts.Find(till.LastInvoiceNumber);
            Ask("and again, for a reprint", () => again?.InvoiceNumber == till.LastInvoiceNumber);

            // ------------------------------------------------------------ the ticket list
            till.LoadTickets();
            Ask("the ticket list is the shop's", () => till.Tickets.Count > 0);
            Ask("and it includes the sale just made",
                () => till.Tickets.Any(t => t.InvoiceNumber == till.LastInvoiceNumber));

            Ask("the reprint screen is offered real numbers", () => Receipts.Recent().Count > 0);
        }
        else
        {
            Say("      (nothing in stock at the shop, so the sale checks were skipped)");
        }

        // ------------------------------------------------------------ nobody gets in for free
        //
        // The server unlocks itself as the owner so its own back office works. The whole point
        // of the token is that this must not reach a request arriving over the network.

        Link.ShopSession.SignOut();

        var refused = false;
        try
        {
            Link.Shop.Categories.List(includeInactive: true);
        }
        catch (Link.NotSignedInAtTheShop)
        {
            refused = true;
        }

        Ask("an unsigned request is refused, not served as the owner", () => refused);

        var wrong = AdminAccount.Opens("not-the-password");
        Ask("a wrong password is refused", () => wrong is false);
        Ask("and refusing it signed nobody in", () => !Link.ShopSession.SignedIn);

        // ------------------------------------------------------------ the back office, remotely
        //
        // The same screens the shop's own machine runs, on a machine with no database. Every
        // one of these would throw if it reached for one.

        Ask("the owner is checked by the shop, not by this machine",
            () => AdminAccount.Opens(OwnerPassword) is not null);

        Ask("signing in gave this machine a token from the shop", () => Link.ShopSession.SignedIn);

        var before = 0;
        Ask("the back office can list the shop's categories", () =>
        {
            before = Link.Shop.Categories.List(includeInactive: true).Count;
            return before >= 0;
        });
        Say($"      {before} categor(y|ies) before");

        var madeId = 0;
        var madeName = "Remote " + DateTime.Now.ToString("HHmmss");
        Ask("and add one", () =>
        {
            madeId = Link.Shop.Categories.Create(madeName, string.Empty, string.Empty);
            return madeId > 0;
        });

        Ask("which the shop then has", () =>
            Link.Shop.Categories.List(includeInactive: true).Any(c => c.Id == madeId));

        Ask("and rename", () =>
        {
            Link.Shop.Categories.Rename(madeId, madeName, madeName + " renamed", string.Empty, string.Empty);
            return Link.Shop.Categories.List(includeInactive: true)
                                       .Any(c => c.Id == madeId && c.Name.EndsWith("renamed"));
        });

        Ask("and delete", () =>
        {
            var went = Link.Shop.Categories.Delete(madeId, madeName, out var why);
            if (!went) Say($"      the shop said: {why}");
            return went;
        });

        Ask("leaving the shop as it was",
            () => Link.Shop.Categories.List(includeInactive: true).Count == before);

        Link.ShopSession.SignOut();
        var afterSignOut = false;
        try
        {
            Link.Shop.Categories.List(includeInactive: true);
        }
        catch (Link.NotSignedInAtTheShop)
        {
            afterSignOut = true;
        }

        Ask("and signing out takes the back office away again", () => afterSignOut);

        // Back in, for the settings check below.
        AdminAccount.Opens(OwnerPassword);

        // ------------------------------------------------------------ what the shop is called
        ShopSettings.Reread();
        var name = AppSettings.Current.BusinessName;
        Ask("the shop's own name reaches the receipt", () => name.Length > 0);
        Say($"      \"{name}\", currency {AppSettings.Current.Currency}");

        return Finish();
    }

    private static bool Ask(string what, Func<bool> check)
    {
        bool ok;
        try
        {
            ok = check();
        }
        catch (Exception problem)
        {
            // The one failure this whole exercise exists to catch.
            var reached = problem is InvalidOperationException
                          && problem.Message.Contains("no shop database");

            Say($"FAIL  {what}: {(reached ? "REACHED FOR A LOCAL DATABASE" : problem.GetType().Name)}");
            Say($"      {problem.Message}");
            _failures++;
            return false;
        }

        Say($"{(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) _failures++;
        return ok;
    }

    private static void Say(string line)
    {
        Log.AppendLine(line);
        Console.WriteLine(line);
    }

    private static int Finish()
    {
        Say(string.Empty);
        Say(_failures == 0
            ? "THE TILL DID A DAY'S WORK WITHOUT A DATABASE"
            : $"{_failures} CHECK(S) FAILED");

        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MarketPos", "tilltest.log");
            System.IO.File.WriteAllText(path, Log.ToString());
        }
        catch { /* the answer is on screen either way */ }

        return _failures;
    }
}
