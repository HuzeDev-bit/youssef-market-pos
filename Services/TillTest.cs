using System.Text;
using MarketPos.Data;
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
    /// Set only once every check has run. A run that stopped early -- the shop never answered,
    /// or it refused to trade against a real one -- has no failures to report and has proved
    /// nothing, and must not sign off as though it had.
    /// </summary>
    private static bool _finished;

    /// <summary>
    /// The password this exercise signs in with. A new install starts with it and this run is
    /// against a scratch shop, so it is the one the server will have.
    /// </summary>
    private static string OwnerPassword = AdminAccount.Starting;

    public static int Run(string address, string? ownerPassword = null)
    {
        if (!string.IsNullOrEmpty(ownerPassword)) OwnerPassword = ownerPassword;

        // Whose books are these?
        //
        // This test does not read a shop; it trades against one. It puts a product on the
        // shelf, takes a delivery, pays a supplier, pays a salary, and rings up a real sale
        // -- and every one of those is a row in whatever database the server it is pointed at
        // happens to own. Pointed at a shop that is actually trading, it fills the owner's
        // books with products called "Remote product 150015" and leaves sales in the day's
        // takings that nobody made.
        //
        // So it refuses unless somebody has said, in words, that the shop on the other end is
        // a throwaway one. --flowtest has asked the same question of the local database since
        // the day it was written; this is the same question asked of a server.
        if (Environment.GetEnvironmentVariable("MARKETPOS_TESTSHOP") != "1")
        {
            Say("REFUSED: --tilltest trades against the shop it is pointed at. It adds products,");
            Say("suppliers, staff and salaries, and rings up a real sale.");
            Say(string.Empty);
            Say("Point it at a throwaway server -- one started with MARKETPOS_DB set to a");
            Say("scratch path -- and set MARKETPOS_TESTSHOP=1 to say so.");
            return 1;
        }

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

        // ---- the shelves ----------------------------------------------------------------

        var shelfId = 0;
        var shelfName = "Remote product " + DateTime.Now.ToString("HHmmss");

        // A barcode of its own, so the price check and the scan have something to find. 299 is
        // not a real GS1 prefix, so it cannot collide with anything the shop actually stocks.
        var shelfBarcode = "299" + DateTime.Now.ToString("HHmmssfff");
        Ask("the back office can list the shop's products", () =>
        {
            Say($"      {Link.Shop.Stock.List(includeInactive: true).Count} product(s)");
            return true;
        });

        Ask("and put a new one on the shelf", () =>
        {
            shelfId = Link.Shop.Stock.Create(new StockItem
            {
                Name = shelfName,
                Barcode = shelfBarcode,
                Category = string.Empty,
                Price = 10m,
                Cost = 6m,
                Unit = Unit.Each,
                ShowInPos = true,
            }, openingStock: 12m);

            return shelfId > 0;
        });

        Ask("which the shop then has, with the stock it was given", () =>
            Link.Shop.Stock.Find(shelfId) is { } made
            && made.Name == shelfName && made.Stock == 12m);

        Ask("and change its price", () =>
        {
            var made = Link.Shop.Stock.Find(shelfId)!;
            Link.Shop.Stock.Update(new StockItem
            {
                Id = made.Id,
                Name = made.Name,
                Category = made.Category,
                Barcode = made.Barcode,
                Price = 11m,
                Cost = made.Cost,
                Unit = made.Unit,
                MinStock = made.MinStock,
                ShowInPos = made.ShowInPos,
            });
            return Link.Shop.Stock.Find(shelfId)?.Price == 11m;
        });

        Ask("and the shelves that need attention",
            () => Link.Shop.Stock.LowStock() is not null
                  && Link.Shop.Stock.OutOfStock() is not null
                  && Link.Shop.Stock.Expiring(30) is not null
                  && Link.Shop.Stock.RecentlyAdded() is not null);

        {
            var had = 0m;
            Ask("and read one product back", () =>
            {
                var one = Link.Shop.Stock.Find(shelfId);
                had = one?.Stock ?? -1m;
                return one is not null;
            });

            Ask("and correct its shelf, which the shop then shows", () =>
            {
                Link.Shop.Stock.Move(shelfId, "test", 3m, StockReason.ManualCorrection,
                                     reference: "Till test", note: "remote check", unitCost: null);
                return Link.Shop.Stock.Find(shelfId)?.Stock == had + 3m;
            });

            Ask("and put it back", () =>
            {
                Link.Shop.Stock.Move(shelfId, "test", -3m, StockReason.ManualCorrection,
                                     reference: "Till test", note: "remote check", unitCost: null);
                return Link.Shop.Stock.Find(shelfId)?.Stock == had;
            });

            Ask("and the correction is in the shop's stock movements", () =>
                Link.Shop.Stock.Movements(productId: shelfId)
                              .Any(m => m.Reference == "Till test"));

            Ask("the shop refuses a correction that would take a shelf below nothing", () =>
            {
                try
                {
                    Link.Shop.Stock.Move(shelfId, "test", -1_000_000m, StockReason.Damaged,
                                         reference: "Till test", note: string.Empty, unitCost: null);
                    return false;
                }
                catch (NotEnoughStockException) { return true; }
            });
        }

        // ------------------------------------------------------------ back to the counter
        //
        // The till's own work, on the product the back office has just put on the shelf. These
        // used to run first, before anything was on it, and skipped themselves on an empty shop
        // -- which is the only kind of shop a fresh machine has.

        Ask("the shop's catalogue comes down again", () => ShopLink.Now(() => ShopLink.PullCatalogue()) >= 0);

        // ------------------------------------------------------------ what is this worth
        var onTheShelf = Catalog.Products.FirstOrDefault(p => p.Id == shelfId);
        Ask("the shop's catalogue now carries it", () => onTheShelf is not null);

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

        // ------------------------------------------------------------ a sale, start to finish
        var sellable = Catalog.Products.FirstOrDefault(p => p.Id == shelfId && p.Stock > 0m);
        Ask("and it can be sold", () => sellable is not null);

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


        Ask("and take the product off the shelf again", () =>
        {
            Link.Shop.Stock.SetActive(shelfId, shelfName, active: false);
            return !Link.Shop.Stock.List().Any(x => x.Id == shelfId);
        });

        // ---- suppliers, deliveries and what we owe them ----------------------------------

        var supplierId = 0;
        var supplierName = "Remote supplier " + DateTime.Now.ToString("HHmmss");
        Ask("the back office can add a supplier", () =>
        {
            supplierId = Link.Shop.Suppliers.Create(new Supplier { Name = supplierName });
            return supplierId > 0;
        });

        Ask("which the shop then lists", () =>
            Link.Shop.Suppliers.List(includeInactive: true).Any(s => s.Id == supplierId));

        Ask("and record a delivery from them", () =>
        {
            var id = Link.Shop.Suppliers.RecordPurchase(new Purchase
            {
                SupplierId = supplierId,
                SupplierName = supplierName,
                PurchasedOn = DateTime.Today,
                Method = "Cash",
                Lines = new List<PurchaseLine>
                {
                    new() { ProductId = shelfId, Name = "test", Quantity = 2m, UnitCost = 1m },
                },
            }, amountPaidNow: 0m);
            return id > 0;
        });

        Ask("and pay them something towards it", () =>
        {
            Link.Shop.Suppliers.Pay(supplierId, supplierName, 1m, DateTime.Today,
                                    method: "Cash", note: "till test");
            return Link.Shop.Suppliers.Payments(supplierId: supplierId).Count > 0;
        });

        Ask("and see the deliveries and what we buy from them",
            () => Link.Shop.Suppliers.Purchases(supplierId: supplierId) is not null
                  && Link.Shop.Suppliers.WhatWeBuy(supplierId) is not null);

        Ask("and put the supplier away again", () =>
        {
            Link.Shop.Suppliers.SetActive(supplierId, supplierName, active: false);
            return !Link.Shop.Suppliers.List().Any(s => s.Id == supplierId);
        });

        // ---- what the shop spends --------------------------------------------------------

        var expenseId = 0;
        Ask("the back office can record an expense", () =>
        {
            expenseId = Link.Shop.Expenses.Create(new Expense
            {
                Name = "Till test",
                Amount = 1m,
                SpentOn = DateTime.Today,
                Method = "Cash",
            });
            return expenseId > 0;
        });

        Ask("which the shop then lists",
            () => Link.Shop.Expenses.List().Any(x => x.Id == expenseId));

        Ask("and total, and break down by category",
            () => Link.Shop.Expenses.Total(Today) >= 0m
                  && Link.Shop.Expenses.ByCategory(Today) is not null
                  && Link.Shop.Expenses.Categories() is not null);

        Ask("and void the one it just wrote", () =>
        {
            Link.Shop.Expenses.Void(expenseId, "Till test", "till test");
            return Link.Shop.Expenses.List().All(x => x.Id != expenseId || x.IsVoid);
        });

        // ---- the people ------------------------------------------------------------------

        var workerId = 0;
        var workerName = "Remote worker " + DateTime.Now.ToString("HHmmss");
        Ask("the back office can add an employee", () =>
        {
            workerId = Link.Shop.Workers.Create(new Worker
            {
                Name = workerName,
                Role = WorkerRole.Cashier,
                Salary = 100m,
            });
            return workerId > 0;
        });

        Ask("and give them a password", () =>
        {
            Link.Shop.Workers.SetPin(workerId, "4321");
            return Link.Shop.Workers.List(includeInactive: true)
                                    .Any(w => w.Id == workerId && w.HasPin);
        });

        Ask("and see the salary ledger and what has been paid",
            () => Link.Shop.Workers.Ledger(Today) is not null
                  && Link.Shop.Workers.PaidIn(Today) >= 0m);

        Ask("and pay a salary", () =>
        {
            Link.Shop.Workers.PaySalary(workerId, workerName, 100m, 100m, Today, DateTime.Today,
                                        method: "Cash", note: "till test");
            return Link.Shop.Workers.PaidIn(Today) > 0m;
        });

        Ask("and stand them down again", () =>
        {
            Link.Shop.Workers.SetActive(workerId, workerName, active: false);
            return !Link.Shop.Workers.List().Any(w => w.Id == workerId);
        });

        // ---- the day's business ----------------------------------------------------------

        Ask("the back office can read the shop's sales history", () =>
        {
            var sales = Link.Shop.Sales.List(Today);
            Say($"      {sales.Count} sale(s) in the range");
            return true;
        });

        Ask("and one sale in full", () =>
        {
            var one = Link.Shop.Sales.List(Today).FirstOrDefault();
            return one is null || Link.Shop.Sales.Find(one.InvoiceNumber) is not null;
        });

        Ask("and which products sold, and who sold them",
            () => Link.Shop.Sales.ProductPerformance(Today) is not null
                  && Link.Shop.Sales.WhoSoldIn(Today) is not null);

        Ask("the back office can read the day's activity", () =>
        {
            var entries = Link.Shop.Activity.List(Today);
            Say($"      {entries.Count} activity entr(y|ies)");
            return true;
        });

        // ---- the dashboard and the reports -----------------------------------------------

        Ask("the dashboard's figures come from the shop", () =>
        {
            var money = Link.Shop.Reports.Money(Today);
            Say($"      revenue {money.Revenue:N2}, profit {money.NetProfit:N2}");
            return true;
        });

        Ask("and its graphs", () =>
            Link.Shop.Reports.Series(Today, SeriesKind.Revenue) is not null
            && Link.Shop.Reports.Series(Today, SeriesKind.Profit) is not null
            && Link.Shop.Reports.Series(Today, SeriesKind.Expenses) is not null
            && Link.Shop.Reports.Series(Today, SeriesKind.SaleCount) is not null);

        Ask("and what it wants the owner to notice", () =>
        {
            var alerts = Link.Shop.Reports.Alerts();
            Say($"      {alerts.Count} alert(s)");
            return true;
        });

        Ask("and where the losses went", () => Link.Shop.Reports.LossesByReason(Today) is not null);

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

        _finished = true;
        return Finish();
    }

    /// <summary>Today, the range the back-office checks ask about.</summary>
    private static DateRange Today => DateRange.For(DatePreset.Today);

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
        Say(_failures == 0 && _finished
            ? "THE TILL DID A DAY'S WORK WITHOUT A DATABASE"
            : _failures == 0
                ? "THE RUN STOPPED EARLY -- IT DID NOT DO A DAY'S WORK"
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
