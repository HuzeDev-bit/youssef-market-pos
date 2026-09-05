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
