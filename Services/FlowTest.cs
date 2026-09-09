using System.Text;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.ViewModels;

namespace MarketPos.Services;

/// <summary>
/// Walks one shop-day through the whole system and checks the numbers at every step.
///
/// This is the test that matters: the individual pages can all render and still disagree
/// about how much money the shop made. Run with <c>MarketPos.exe --flowtest</c> against a
/// scratch database (set MARKETPOS_DB), it buys stock on credit, sells some of it, refunds
/// part of a sale, writes off a breakage, pays a supplier, pays a salary and books an
/// expense — then asserts revenue, COGS, gross profit, net profit, money spent, supplier
/// debt and stock levels all come out where they should.
/// </summary>
public static class FlowTest
{
    private static readonly StringBuilder Log = new();
    private static int _failures;

    public static int Run()
    {
        // This test buys, sells, refunds, pays a salary and books an expense — all of it real
        // writes. Run without a scratch database it does that to the shop's own books, and
        // the fake numbers then turn up in the owner's profit. Nothing here is worth that, so
        // it refuses rather than trusting whoever typed the command to have set the variable.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MARKETPOS_DB")))
        {
            Console.WriteLine("REFUSED: --flowtest writes real rows, so it needs a scratch database.");
            Console.WriteLine("Set MARKETPOS_DB to a throwaway path and run it again.");
            return 2;
        }

        Session.UnlockAsOwner();
        Database.Initialize();

        // A database this test has already been through still holds its fixtures, and creating
        // them a second time trips the unique barcode. That surfaced as WPF's error dialog on a
        // headless run: no output, no exit, just a process sitting there looking busy until the
        // timeout killed it. Say so instead.
        if (StockRepository.FindByBarcode("9990000000001") is not null)
        {
            Console.WriteLine("REFUSED: this database has already been through --flowtest.");
            Console.WriteLine("Delete it, or point MARKETPOS_DB at a path that does not exist yet.");
            return 2;
        }

        var supplierId = SupplierRepository.Create(new Supplier { Name = "Test Wholesaler" });
        var workerId = WorkerRepository.Create(new Worker
        {
            Name = "Test Cashier",
            Role = WorkerRole.Cashier,
            Salary = 3000m,
            SalaryPeriod = SalaryPeriod.Monthly,
            StartedOn = DateTime.Today,
        });

        var productId = StockRepository.Create(new StockItem
        {
            Barcode = "9990000000001",
            Name = "Test Cola",
            Category = "Drinks",
            Cost = 4m,
            Price = 6m,
            MinStock = 5m,
        });

        Check("opening stock is zero", StockRepository.Find(productId)!.Stock, 0m);

        // ---- A delivery arrives: 100 units at 4 DH, 200 DH paid of the 400 owed ----
        SupplierRepository.RecordPurchase(new Purchase
        {
            SupplierId = supplierId,
            SupplierName = "Test Wholesaler",
            PurchasedOn = DateTime.Today,
            Lines =
            {
                new PurchaseLine { ProductId = productId, Name = "Test Cola", Quantity = 100m, UnitCost = 4m },
            },
        }, amountPaidNow: 200m);

        Check("stock after delivery", StockRepository.Find(productId)!.Stock, 100m);
        Check("supplier still owed", SupplierRepository.List().Single().Owed, 200m);

        // No sell price was named on that line, so the shelf price must be untouched. This is
        // the quiet failure: a blank box read as zero would make everything free.
        Check("a delivery with no sell price leaves the shelf alone",
              StockRepository.Find(productId)!.Price, 6m);

        // ---- Sell 10 at 6 DH ----
        Catalog.Reload();
        var product = Catalog.Products.First(p => p.Barcode == "9990000000001");

        var cart = new List<SaleItem> { new(product, 10m) };
        var invoice = SaleRepository.Save(cart, 60m, DiscountKind.None, 0m, 0m, 60m, 0m, 60m,
                                          PaymentMethod.Cash, 60m);

        Check("stock after sale", StockRepository.Find(productId)!.Stock, 90m);

        var today = DateRange.For(DatePreset.Today);
        Check("revenue after sale", Finance.For(today).Revenue, 60m);
        Check("COGS after sale", Finance.For(today).Cogs, 40m);
        Check("gross profit after sale", Finance.For(today).GrossProfit, 20m);

        // ---- The cost price changes. Last month's profit must not move with it. ----
        var stock = StockRepository.Find(productId)!;
        StockRepository.Update(new StockItem
        {
            Id = stock.Id, Barcode = stock.Barcode, Name = stock.Name, Category = stock.Category,
            Cost = 5m, Price = stock.Price, MinStock = stock.MinStock, Unit = stock.Unit,
            TaxRate = stock.TaxRate,
        });
        Check("COGS is frozen at the price paid", Finance.For(today).Cogs, 40m);

        // ---- Refund 2 of the 10, back into stock ----
        var sale = SalesHistoryRepository.Find(invoice)!;
        SalesHistoryRepository.Refund(invoice,
            new[] { (sale.Lines[0].Id, 2m) }, "Customer changed their mind", restock: true);

        Check("stock after refund", StockRepository.Find(productId)!.Stock, 92m);
        Check("revenue after refund", Finance.For(today).Revenue, 48m);
        Check("COGS after refund", Finance.For(today).Cogs, 32m);
        Check("gross profit after refund", Finance.For(today).GrossProfit, 16m);

        // ---- Break 3 bottles. Cost is now 5, so the write-off is valued at 15. ----
        InventoryRepository.RecordLoss(productId, "Test Cola", 3m, StockReason.Damaged);
        Check("stock after breakage", StockRepository.Find(productId)!.Stock, 89m);
        Check("stock losses", Finance.For(today).StockLosses, 15m);

        // ---- Costs: 100 to the supplier, 500 salary, 300 rent ----
        SupplierRepository.Pay(supplierId, "Test Wholesaler", 100m, DateTime.Today);
        WorkerRepository.PaySalary(workerId, "Test Cashier", 3000m, 500m, today, DateTime.Today);
        ExpenseRepository.Create(new Expense
        {
            Name = "Rent", Amount = 300m, SpentOn = DateTime.Today,
            CategoryId = ExpenseRepository.AddCategory("Rent"),
        });

        var f = Finance.For(today);
        Check("supplier debt after payment", SupplierRepository.TotalOwed(), 100m);
        Check("operating expenses", f.OperatingExpenses, 300m);
        Check("salary expense", f.SalaryExpense, 500m);

        // Net profit = gross 16 − rent 300 − salary 500 − breakage 15
        Check("net profit", f.NetProfit, -799m);

        // Money spent = supplier payments (200 + 100) + rent 300 + salary 500.
        // The 400 delivery is NOT in here: only 300 of it has actually been paid.
        Check("money spent", f.MoneySpent, 1100m);
        Check("stock received is separate from money spent", f.StockPurchased, 400m);

        Check("cash collected", f.CashCollected, 48m);
        Check("sale count", f.SaleCount, 1);

        var (lines, total, received) = Finance.MoneySpent(today);
        Check("money-spent lines add up", lines.Sum(l => l.Amount), total);
        Check("money-spent total", total, 1100m);
        Check("stock shown separately", received, 400m);

        // ---- The audit trail caught all of it ----
        var log = ActivityRepository.List(today, limit: 200);
        Check("activity recorded", log.Count >= 9 ? 1 : 0, 1);

        // Delivery, sale, return, breakage. No opening-stock movement: the product was
        // created with zero, and a movement of nothing would be noise in the ledger.
        var movements = InventoryRepository.ListMovements(today, productId);
        Check("stock movements recorded", movements.Count, 4);
        Check("movement chain ends at the current stock",
              movements.First().AfterQty, StockRepository.Find(productId)!.Stock);

        CheckTillCanAddProducts();
        CheckRemovingAProductKeepsItsHistory(productId);

        Console.WriteLine(Log.ToString());
        Console.WriteLine(_failures == 0 ? "FLOW TEST PASSED" : $"{_failures} CHECKS FAILED");
        return _failures;
    }

    /// <summary>
    /// A cashier must be able to create the product in their hand so the customer can pay for
    /// it — and must NOT be able to reprice the rest of the shop. Hiding the button would not
    /// be enough; the split has to hold at the repository.
    /// </summary>
    private static void CheckTillCanAddProducts()
    {
        var cashierId = WorkerRepository.Create(new Worker
        {
            Name = "Till Cashier",
            Role = WorkerRole.Cashier,
            StartedOn = DateTime.Today,
        });

        // Drop the owner unlock first: holding the admin password outranks whoever is signed
        // in at the till, so the cashier's limits cannot be seen while it is still held.
        Session.SignOut();
        Session.SignIn(WorkerRepository.Find(cashierId)!);

        Check("cashier may add a product at the till", Session.Can(Permission.AddProductAtTill) ? 1 : 0, 1);
        Check("cashier may not manage the catalogue", Session.Can(Permission.ManageProducts) ? 1 : 0, 0);
        Check("cashier may not see profit", Session.Can(Permission.SeeFinancials) ? 1 : 0, 0);

        // Sold by weight, priced per kilo, with the weight in hand as its opening stock.
        var id = StockRepository.Create(new StockItem
        {
            Barcode = "9990000000002",
            Name = "Loose Olives",
            Category = "Produce",
            Price = 38m,
            Unit = Unit.Kg,
        }, openingStock: 2.5m);

        var created = StockRepository.Find(id)!;
        Check("product created from the till", created.Price, 38m);
        Check("weighed goods keep their unit", created.Unit == Unit.Kg ? 1 : 0, 1);
        Check("opening stock is what the cashier weighed", created.Stock, 2.5m);

        // The same cashier must be refused an edit.
        var refused = 0;
        try { StockRepository.Update(created); }
        catch (UnauthorizedAccessException) { refused = 1; }
        Check("cashier is refused a catalogue edit", refused, 1);

        // The owner's password has to win over whoever is at the till, or unlocking the back
        // office during a cashier's shift would leave every page refusing the owner.
        Session.UnlockAsOwner();
        Check("owner unlock outranks the signed-in cashier",
              Session.Can(Permission.SeeFinancials) ? 1 : 0, 1);

        Session.SignOut();
        Session.UnlockAsOwner();

        RepricingOnDelivery(SupplierRepository.List().First().Id);
        TheBillsAreNotStock();
        ASaleHandedOverByATill();
        AnOrdinaryDayForACashier();
        SellingTheLastOne();
        ThirtyOnTheShelf();
    }

    /// <summary>
    /// The whole of a cashier's day, done as a cashier.
    ///
    /// Every other test in this file runs as the owner, because the owner can do everything —
    /// which is exactly why they prove nothing about the person who actually stands at the
    /// till all day. This one signs in as a cashier and does only what a cashier does: find
    /// the thing, check what it costs, sell it, take the money.
    ///
    /// The two failures it guards are opposite and both quiet. A permission missing from the
    /// cashier grant stops the shop trading, in front of a customer, with no way round it. A
    /// permission wrongly included hands the shop's margins to whoever is on the counter.
    /// </summary>
    private static void AnOrdinaryDayForACashier()
    {
        // A real cashier with a real password, made the way the back office makes one.
        Session.UnlockAsOwner();
        var cashierId = WorkerRepository.Create(new Worker
        {
            Name = "Amina",
            Role = WorkerRole.Cashier,
            Salary = 2500m,
            SalaryPeriod = SalaryPeriod.Monthly,
            StartedOn = DateTime.Today,
        });
        WorkerRepository.SetPin(cashierId, "4417");

        Check("a wrong password opens nothing",
              WorkerRepository.SignIn(cashierId, "0000") is null ? 1 : 0, 1);

        var cashier = WorkerRepository.SignIn(cashierId, "4417");
        Check("the right one signs the cashier in", cashier is not null ? 1 : 0, 1);

        Session.SignOut();                    // drops the owner unlock
        Session.SignIn(cashier!);

        Check("and the till knows who is on it",
              Session.CurrentRole == WorkerRole.Cashier ? 1 : 0, 1);

        Catalog.Reload();
        var product = Catalog.Products.First(p => p.Barcode == "9990000000001");
        var stockBefore = StockRepository.Find(product.Id)!.Stock;

        // ---- scanning to check a price, which is not a sale ----
        var checked_ = PriceCheck.For(product.Barcode);
        Check("a cashier can scan to see what something costs",
              checked_ is { Found: true } && checked_.PriceText.Length > 0 ? 1 : 0, 1);
        Check("but not what the shop paid for it", checked_.ShowsCost ? 1 : 0, 0);
        Check("and checking sold nothing", StockRepository.Find(product.Id)!.Stock, stockBefore);

        // ---- the sale itself ----
        var line = new SaleItem(product, 3m);
        var invoice = SaleRepository.Save(
            new[] { line }, line.LineTotal, DiscountKind.None, 0m, 0m,
            line.LineTotal, 0m, line.LineTotal, PaymentMethod.Cash, 20m);

        Check("a cashier can complete a sale", invoice > 0 ? 1 : 0, 1);
        Check("it takes the goods off the shelf",
              StockRepository.Find(product.Id)!.Stock, stockBefore - 3m);

        var saved = SalesHistoryRepository.List(DateRange.For(DatePreset.ThisYear))
            .First(x => x.InvoiceNumber == invoice);
        Check("and it is recorded against their name",
              saved.CashierLabel.Contains("Amina") ? 1 : 0, 1);

        // ---- and the receipt they hand over ----
        var receipt = SaleRepository.FindByInvoiceNumber(invoice);
        Check("the receipt reads back for reprinting", receipt is not null ? 1 : 0, 1);
        Check("with the change owed on it", receipt!.ChangeGiven, 20m - line.LineTotal);

        // ---- what the counter may not do ----
        (string What, Action Do)[] forbidden =
        [
            ("reprice the shop", () => StockRepository.Update(StockRepository.Find(product.Id)!)),
            ("read the profit", () => Session.Require(Permission.SeeFinancials)),
            ("see what staff are paid", () => Session.Require(Permission.SeeSalaries)),
            ("pay a salary", () => Session.Require(Permission.PaySalaries)),
            ("change the shop's settings", () => Session.Require(Permission.ManageSettings)),
            ("write off stock", () => Session.Require(Permission.SeeStockMovements)),
        ];

        var allowed = new List<string>();
        foreach (var (what, act) in forbidden)
        {
            try { act(); allowed.Add(what); }
            catch (UnauthorizedAccessException) { }
        }

        Check($"the counter cannot {string.Join(", ", forbidden.Select(f => f.What))}",
              allowed.Count, 0);
        if (allowed.Count > 0) Log.AppendLine($"      allowed: {string.Join(", ", allowed)}");

        Session.SignOut();
        Session.UnlockAsOwner();
    }

    /// <summary>
    /// Bills are operating expenses and deliveries are not.
    ///
    /// This is the one an owner gets wrong by hand: stock bought is money gone, so it feels
    /// like an expense — but the goods are still there, and charging it as an expense now and
    /// as cost of goods when it sells charges the shop twice for the same tin. Worth holding,
    /// because nothing on screen would look wrong if it broke.
    /// </summary>
    private static void TheBillsAreNotStock()
    {
        var month = DateRange.For(DatePreset.ThisMonth);
        var before = ExpenseRepository.Total(month);

        var kinds = ExpenseRepository.Categories();
        int Kind(string name) => kinds.First(c => c.Name == name).Id;

        foreach (var (name, kind, amount) in new[]
                 {
                     ("Shop rent", "Rent", 2000m),
                     ("Electricity", "Electricity", 350m),
                     ("Internet", "Internet", 250m),
                 })
        {
            ExpenseRepository.Create(new Expense
            {
                Name = name,
                CategoryId = Kind(kind),
                Category = kind,
                Amount = amount,
                SpentOn = DateTime.Today,
                Recurring = Recurrence.Monthly,
            });
        }

        Check("the bills add up", ExpenseRepository.Total(month) - before, 2600m);

        var byKind = ExpenseRepository.ByCategory(month);
        Check("rent is the biggest bill",
              byKind.OrderByDescending(k => k.Amount).First().Category == "Rent" ? 1 : 0, 1);

        // The deliveries this test recorded are nowhere in that figure.
        Check("stock bought is not an expense",
              byKind.Any(k => k.Category is "Rent" or "Electricity" or "Internet") &&
              !byKind.Any(k => k.Amount == 400m) ? 1 : 0, 1);
    }

    /// <summary>
    /// A delivery is when the owner finds out what they paid, so it is also when they set what
    /// to charge. Run last and against its own product, because it buys stock and pays money —
    /// dropped into the middle of the flow it would move every total asserted after it.
    /// </summary>
    private static void RepricingOnDelivery(int supplierId)
    {
        var productId = StockRepository.Create(new StockItem
        {
            Barcode = "9990000000003",
            Name = "Test Biscuits",
            Category = "Pantry",
            Cost = 2m,
            Price = 3m,
        });

        // No sell price named: the shelf price must be untouched. This is the quiet failure —
        // an empty box read as zero would make the product free.
        SupplierRepository.RecordPurchase(new Purchase
        {
            SupplierId = supplierId,
            SupplierName = "Test Wholesaler",
            PurchasedOn = DateTime.Today,
            Lines =
            {
                new PurchaseLine { ProductId = productId, Name = "Test Biscuits", Quantity = 10m, UnitCost = 2m },
            },
        }, amountPaidNow: 0m);

        Check("a delivery with no sell price leaves the shelf alone",
              StockRepository.Find(productId)!.Price, 3m);

        // Now one that reprices as it arrives.
        SupplierRepository.RecordPurchase(new Purchase
        {
            SupplierId = supplierId,
            SupplierName = "Test Wholesaler",
            PurchasedOn = DateTime.Today,
            Lines =
            {
                new PurchaseLine
                {
                    ProductId = productId, Name = "Test Biscuits",
                    Quantity = 5m, UnitCost = 2.50m, SellPrice = 4m,
                },
            },
        }, amountPaidNow: 0m);

        var after = StockRepository.Find(productId)!;
        Check("a delivery can set the selling price", after.Price, 4m);
        Check("the delivered cost becomes the product cost", after.Cost, 2.50m);
        Check("stock went up by both deliveries", after.Stock, 15m);

        // A product the shop has never sold, typed straight onto the delivery. The van brings
        // something new and it has to go somewhere; a line with no product id says so.
        var before = StockRepository.List().Count;

        SupplierRepository.RecordPurchase(new Purchase
        {
            SupplierId = supplierId,
            SupplierName = "Test Wholesaler",
            PurchasedOn = DateTime.Today,
            Lines =
            {
                new PurchaseLine
                {
                    ProductId = 0, Name = "Test Dates",
                    Quantity = 8m, UnitCost = 20m, SellPrice = 30m,
                },
            },
        }, amountPaidNow: 0m);

        var made = StockRepository.List().FirstOrDefault(p => p.Name == "Test Dates");

        Check("a name typed on a delivery becomes a product",
              made is null ? 0 : 1, 1);
        Check("the shop has one more product", StockRepository.List().Count, before + 1);
        Check("the new product starts with what arrived", made?.Stock ?? 0m, 8m);
        Check("the new product keeps what it cost", made?.Cost ?? 0m, 20m);
        Check("the new product keeps what it sells for", made?.Price ?? 0m, 30m);
        // A delivery scanned in keeps the code that was on the box. Without this the product
        // was given a fresh in-store code, so the first time a customer brought one to the
        // till the scan found nothing and the cashier had to search by name.
        SupplierRepository.RecordPurchase(new Purchase
        {
            SupplierId = supplierId,
            SupplierName = "Test Wholesaler",
            PurchasedOn = DateTime.Today,
            Lines =
            {
                new PurchaseLine
                {
                    ProductId = 0, Name = "Test Coffee", Barcode = "6111999000123",
                    Quantity = 4m, UnitCost = 12m, SellPrice = 18m,
                },
            },
        }, amountPaidNow: 0m);

        var scanned = StockRepository.List().FirstOrDefault(p => p.Name == "Test Coffee");
        Check("a scanned delivery keeps the code on the box",
              scanned?.Barcode == "6111999000123" ? 1 : 0, 1);

        Check("the new product gets an in-store barcode",
              (made?.Barcode.Length ?? 0) == 13 && made!.Barcode.StartsWith('2') ? 1 : 0, 1);

        // A product with no category goes to Other, not to a category called "". The empty
        // name drew a nameless card in the back office and a blank shelf on the till, and
        // nothing looked wrong until somebody went looking for it.
        Check("an unfiled product goes to a shelf with a name",
              made!.Category == "Other" ? 1 : 0, 1);
        Check("no nameless category is invented",
              CategoryRepository.List(includeInactive: true).Any(c => c.Name.Trim().Length == 0) ? 1 : 0, 0);
    }

    /// <summary>
    /// A sale rung up on a cashier's machine and handed to the server.
    ///
    /// The till cannot know whether a request that timed out was written, so it sends the same
    /// sale again. Everything here turns on that being harmless: a second copy would take the
    /// customer's money twice in the books, move the stock twice, and pay the shop a profit it
    /// never made — and nothing on any screen would look wrong.
    /// </summary>
    private static void ASaleHandedOverByATill()
    {
        Catalog.Reload();
        var product = Catalog.Products.First(p => p.Barcode == "9990000000001");
        var stockBefore = StockRepository.Find(product.Id)!.Stock;
        var salesBefore = SalesHistoryRepository.List(DateRange.For(DatePreset.ThisYear)).Count;

        var lines = new List<SaleItem> { new(product, 2m) };

        // Rung up an hour ago by somebody who is not signed in on this machine.
        var soldAt = DateTime.Now.AddHours(-1);
        var origin = new SaleOrigin(soldAt, null, "Fatima at till 1", "till-1/000042");

        var first = SaleRepository.Save(lines, 12m, DiscountKind.None, 0m, 0m,
                                        12m, 0m, 12m, PaymentMethod.Cash, 12m, origin);

        Check("a sale from a till is written", first > 0 ? 1 : 0, 1);
        Check("it took the goods off the shelf",
              StockRepository.Find(product.Id)!.Stock, stockBefore - 2m);

        // The same sale again, exactly as a till retries it.
        var again = SaleRepository.Save(lines, 12m, DiscountKind.None, 0m, 0m,
                                        12m, 0m, 12m, PaymentMethod.Cash, 12m, origin);

        Check("a retry returns the first invoice", again, first);
        Check("a retry adds no second sale",
              SalesHistoryRepository.List(DateRange.For(DatePreset.ThisYear)).Count, salesBefore + 1);
        Check("a retry moves no more stock",
              StockRepository.Find(product.Id)!.Stock, stockBefore - 2m);

        // Attribution: the sale belongs to whoever rang it up, at the moment it happened.
        var saved = SalesHistoryRepository.List(DateRange.For(DatePreset.ThisYear))
            .First(x => x.InvoiceNumber == first);

        Check("it is credited to the cashier who rang it up",
              saved.CashierLabel.Contains("Fatima") ? 1 : 0, 1);
        Check("it is dated when it happened, not when it arrived",
              Math.Abs((saved.SoldAt - soldAt).TotalMinutes) < 1 ? 1 : 0, 1);
    }

    /// <summary>
    /// Selling down to the last one, and then trying to sell one more.
    ///
    /// The bug this holds shut: stock was a signed sum with no floor, so a shop with thirty
    /// tins could sell forty and the count read minus ten afterwards — quietly wrong in the
    /// stock value, in the reorder list, and in the cost of goods for ever after. Nothing on
    /// any screen looked broken.
    /// </summary>
    private static void SellingTheLastOne()
    {
        Session.UnlockAsOwner();

        var id = StockRepository.Create(new StockItem
        {
            Barcode = "9990000000777",
            Name = "Last Tin",
            Category = "Test",
            Cost = 4m,
            Price = 10m,
            MinStock = 1m,
        }, openingStock: 3m);

        Check("the shelf starts with three", StockRepository.Find(id)!.Stock, 3m);

        Catalog.Reload();
        var product = Catalog.Products.First(p => p.Id == id);
        var line = new List<SaleItem> { new(product, 3m) };

        // All three, which is allowed exactly once.
        SaleRepository.Save(line, 30m, DiscountKind.None, 0m, 0m, 30m, 0m, 30m,
                            PaymentMethod.Cash, 30m);

        Check("selling all three empties the shelf", StockRepository.Find(id)!.Stock, 0m);
        Check("and the shop calls it out of stock",
              StockRepository.Find(id)!.Status == StockStatus.OutOfStock ? 1 : 0, 1);

        // The fourth. The goods left the shop whatever the count says, so the sale is banked
        // and the count goes negative — which is the shop saying its count is behind, not the
        // shop losing a sale.
        SaleRepository.Save(new List<SaleItem> { new(product, 1m) }, 10m, DiscountKind.None,
                            0m, 0m, 10m, 0m, 10m, PaymentMethod.Cash, 10m);

        Check("selling one more is still banked", StockRepository.Find(id)!.Stock, -1m);
        Check("and it is still counted as out of stock",
              StockRepository.Find(id)!.Status == StockStatus.OutOfStock ? 1 : 0, 1);

        // A loss is a different matter. Nothing has happened yet, so a number that cannot be
        // true is somebody mistyping, and there is time to say so.
        var lossRefused = false;
        var message = string.Empty;
        try { InventoryRepository.RecordLoss(id, "Last Tin", 5m, StockReason.Damaged); }
        catch (NotEnoughStockException error) { lossRefused = true; message = error.Message; }

        Check("writing off more than is there is still refused", lossRefused ? 1 : 0, 1);
        Log.AppendLine($"      the shop would say: {message}");

        // And that refusal must not have half-written anything.
        Check("a refused write-off leaves the count alone", StockRepository.Find(id)!.Stock, -1m);
    }

    /// <summary>
    /// The shop's own report, scan by scan: thirty on the shelf, sold one at a time, and a
    /// thirty-first that goes through because the cashier is holding it.
    ///
    /// Driven through the till's own view model rather than through the repository, so what is
    /// being tested is the thing a cashier actually touches — the scan, the basket, the sale.
    /// </summary>
    private static void ThirtyOnTheShelf()
    {
        Session.UnlockAsOwner();

        var id = StockRepository.Create(new StockItem
        {
            Barcode = "6119999900030",
            Name = "Thirty Tins",
            Category = "Test",
            Cost = 6m,
            Price = 9m,
            MinStock = 5m,
        }, openingStock: 30m);

        Catalog.Reload();
        var till = new ViewModels.SaleViewModel();

        // Thirty scans, one at a time, exactly as the counter would.
        for (var i = 0; i < 30; i++)
        {
            till.SearchText = "6119999900030";
            till.SubmitBarcodeCommand.Execute(null);
        }

        Check("thirty scans put thirty on the sale", till.Cart.FirstOrDefault()?.Quantity ?? 0m, 30m);

        // The thirty-first. The shelf says thirty, the cashier has thirty-one in their hands,
        // and the customer is waiting: the sale wins. Stopping it would not put the goods
        // back, it would only lose the record of them leaving.
        till.SearchText = "6119999900030";
        till.SubmitBarcodeCommand.Execute(null);

        Check("the thirty-first goes on the sale too", till.Cart.FirstOrDefault()?.Quantity ?? 0m, 31m);
        Check("and nothing red is said about it", till.StatusIsError ? 1 : 0, 0);

        // Bank it.
        till.CompleteSale(PaymentMethod.Cash, 279m);

        Check("the count goes one below zero, and says so", StockRepository.Find(id)!.Stock, -1m);
        Check("the shop calls it out of stock",
              StockRepository.Find(id)!.Status == StockStatus.OutOfStock ? 1 : 0, 1);

        // And now a scan of something the shop has none of.
        Catalog.Reload();
        var after = new ViewModels.SaleViewModel();
        after.SearchText = "6119999900030";
        after.SubmitBarcodeCommand.Execute(null);

        // Now the basket is empty, so the shelf does have the last word again.
        Check("scanning it on a fresh sale is refused", after.Cart.Count, 0);
        Check("and that refusal is said out loud", after.StatusIsError ? 1 : 0, 1);
        Log.AppendLine($"      the till says: {after.StatusMessage}");

        // A barcode the shop has never seen.
        after.SearchText = "1234567890123";
        after.SubmitBarcodeCommand.Execute(null);
        Check("an unknown barcode adds nothing either", after.Cart.Count, 0);
        Log.AppendLine($"      the till says: {after.StatusMessage}");
    }

    /// <summary>
    /// Taking a product off the shelf, and the promise that goes with it.
    ///
    /// The Inventory page can now remove a product, which is the thing the shop asked for and
    /// also the thing most likely to be implemented as a DELETE by whoever comes next. It must
    /// not be. This product has a sale, a refund and a breakage against it, and its cost price
    /// is what every one of those figures was worked out from — so removing it has to take it
    /// off the shelf and leave the books exactly where they were.
    ///
    /// The barcode is the other half. A cashier holding a withdrawn line still has to be told
    /// what it is, rather than that the shop has never heard of a thing it sold last week.
    /// </summary>
    private static void CheckRemovingAProductKeepsItsHistory(int productId)
    {
        var today = DateRange.For(DatePreset.Today);
        var before = Finance.For(today);
        var product = StockRepository.Find(productId)!;
        var movements = InventoryRepository.ListMovements(today, productId).Count;

        StockRepository.SetActive(productId, product.Name, active: false);

        Check("a removed product leaves the stock list",
              StockRepository.List().Count(i => i.Id == productId), 0);
        Check("but is still there when asked for",
              StockRepository.List(includeInactive: true).Count(i => i.Id == productId), 1);
        Check("and still answers a scan of its barcode",
              StockRepository.FindByBarcode(product.Barcode)?.Id ?? 0, productId);

        // The whole reason it is hidden rather than deleted.
        var after = Finance.For(today);
        Check("revenue is untouched by removing it", after.Revenue, before.Revenue);
        Check("COGS is untouched", after.Cogs, before.Cogs);
        Check("net profit is untouched", after.NetProfit, before.NetProfit);
        Check("its stock movements are all still there",
              InventoryRepository.ListMovements(today, productId).Count, movements);

        // And back again, which is what makes the button safe to press.
        StockRepository.SetActive(productId, product.Name, active: true);
        Check("putting it back puts it back on the shelf",
              StockRepository.List().Count(i => i.Id == productId), 1);
        Check("with its count intact", StockRepository.Find(productId)!.Stock, product.Stock);
    }

    private static void Check(string what, decimal actual, decimal expected)
    {
        if (Math.Abs(actual - expected) < 0.005m)
        {
            Log.AppendLine($"ok    {what} = {actual:0.##}");
        }
        else
        {
            _failures++;
            Log.AppendLine($"FAIL  {what}: expected {expected:0.##}, got {actual:0.##}");
        }
    }
}
