using System.Globalization;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Link;
using MarketPos.Services;

// ============================================================================
// The shop's server.
//
// Runs on the back-office machine and answers the tills over the shop's own network. It owns
// marketpos.db; no other machine opens that file. SQLite over a Windows share has unreliable
// locking and is a known way to corrupt a database, and this one holds the shop's money.
//
// Every endpoint calls the same repository the back office calls. Nothing here re-implements a
// rule about cost, stock or profit — a second implementation would be a second answer.
// ============================================================================

// A console window that opens and shuts itself has told nobody anything, and the machine this
// runs on is a box in the back that nobody is watching. So everything this server does at
// startup, and anything that stops it, is written down where it can be read afterwards.
var diary = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarketPos",
    "server.log");

void Note(string line)
{
    try
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(diary)!);
        System.IO.File.AppendAllText(diary, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
    }
    catch { /* a server that cannot write its diary still has a shop to serve */ }
}

Note($"--- starting, from {Environment.ProcessPath}");

// Catches what the try around Run cannot: anything thrown while the server is being built, on
// a background thread, or after it is up.
AppDomain.CurrentDomain.UnhandledException += (_, fatal) =>
{
    Note("FATAL " + fatal.ExceptionObject);

    Console.WriteLine();
    Console.WriteLine("The shop server stopped.");
    Console.WriteLine((fatal.ExceptionObject as Exception)?.Message ?? "Unknown error.");
    Console.WriteLine();
    Console.WriteLine($"Written down in {diary}");
    Console.WriteLine("Press any key to close.");

    if (!Console.IsInputRedirected)
    {
        try { Console.ReadKey(true); } catch { /* nobody there */ }
    }
};

var builder = WebApplication.CreateBuilder(args);

// Listens on the shop's network, not just on this machine. Kestrel's default is localhost,
// which works perfectly on the developer's laptop and is invisible to every till in the shop —
// the failure looks like a broken cable and is a one-line setting. Overridable, so a shop that
// needs another port can still pass --urls.
if (!args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase))
    && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
{
    // Encrypted, because the owner's password and every session token afterwards cross this
    // wire. The certificate is the shop's own — see ShopCertificate — and each till pins it the
    // first time it connects, so nothing else on the network can answer for the shop.
    var ours = MarketPos.Link.ShopCertificate.Ours();

    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        kestrel.ListenAnyIP(5000, listen => listen.UseHttps(ours));
    });

    Note($"certificate fingerprint {MarketPos.Link.ShopCertificate.Fingerprint(ours)}");
}

builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// The database is opened once at start rather than on the first request, so a shop with a
// broken install finds out when it starts the server, not when a customer is waiting.
Database.Initialize();

// And the back office starts behind the password every install ships with.
//
// Only the all-in-one used to do this, so a shop running this server had no owner password set
// at all -- and a password that is not set is not a password anyone can get wrong. Any till on
// the network could sign in as the owner by typing anything. The owner is told to change it on
// the sign-in screen, but "not set yet" must never be the same as "open to everybody".
AdminAccount.StartWithTheDefault();

// Every request runs as the shop itself. The till proves who its cashier is on sign-in, and
// that name is carried on each sale it hands over — but the server's own authority to write
// does not come from whoever is standing at a till.
Session.UnlockAsOwner();

var serverId = Environment.MachineName;

// ---------------------------------------------------------------- who am I

app.MapGet("/hello", () => new Hello(
    AppSettings.Current.BusinessName, Contracts.Version, serverId, DateTime.Now));

// ---------------------------------------------------------------- who is at the till

// Staff, as a till needs them — with their password hashes, so a cashier can still sign in
// with this machine switched off. See StaffMember for why that is the right trade.
app.MapGet("/staff", () => WorkerRepository.ForSync()
    .Select(w => new StaffMember(w.Id, w.Name, w.Role, w.Hash, w.Salt, w.IsActive))
    .ToList());

app.MapPost("/signin", (SignInRequest request) =>
{
    // Checked here, never on the till: a password that travelled to the counter to be compared
    // there would be a password the counter had.
    var worker = WorkerRepository.SignIn(request.WorkerId, request.Password);

    return worker is null
        ? Results.Unauthorized()
        : Results.Ok(new SignedIn(worker.Id, worker.Name, worker.Role.ToString()));
});

// ---------------------------------------------------------------- the catalogue

app.MapGet("/catalog", (string? since) =>
{
    var items = StockRepository.List()
        .Where(p => p.ShowInPos)
        .Select(p => new CatalogItem(
            p.Id, p.Barcode, p.Name, p.Category, p.Price, p.TaxRate, p.Unit.ToString(), p.Stock,
            HasPhoto: !string.IsNullOrWhiteSpace(p.ImagePath)))
        .ToList();

    // A stamp rather than a row count: the till sends back what it last saw, and a shop that
    // deleted a product has a different stamp even though the count is unchanged.
    var stamp = Stamp(items);

    // Nothing changed since the till last looked, so it keeps what it has. On a shop with a
    // few hundred products this is the difference between a moment and a wait at every start.
    if (!string.IsNullOrEmpty(since) && since == stamp)
        return Results.Ok(new CatalogPage(stamp, Array.Empty<CatalogItem>(), Complete: false));

    return Results.Ok(new CatalogPage(stamp, items, Complete: true));
});

// ---------------------------------------------------------------- products coming in

// A cashier scanned something the shop does not sell and filled its details in at the counter.
// It is created here, in the shop's own database, so that it exists for the back office, the
// stock list and every other till the moment it is saved — and so that the record of who added
// it and when is written in the one place that keeps such records.
app.MapPost("/products/scanned", (NewProduct arriving) =>
{
    var made = ShopTill.AddScanned(arriving, out var problem);
    if (made is null) return Results.BadRequest(problem);

    Note(made.AlreadyHad
        ? $"product {made.Barcode} was already here as {made.Name}"
        : $"{arriving.AddedBy} added {made.Name} "
          + $"({(made.Barcode.Length > 0 ? made.Barcode : "no barcode")}) from a till");

    return Results.Ok(made);
});

// ---------------------------------------------------------------- what a till asks for
//
// A cashier's machine holds no books, so everything on its screen is asked for here. The
// answers themselves live in ShopData, written once, so this server and the all-in-one cannot
// tell a till two different things about the same shop.

// What is this and what does it cost. Never touches the sale.
app.MapGet("/pricecheck", (string? q, bool? owner) =>
    Results.Ok(ShopData.PriceCheck(q ?? string.Empty, owner == true)));

// The ticket list, today's takings, and the numbers a reprint offers.
app.MapGet("/tickets", (string? search) => Results.Ok(ShopData.Tickets(search)));

// One ticket, whole, for printing and reprinting.
app.MapGet("/tickets/{invoice:int}", (int invoice) =>
    ShopData.Ticket(invoice) is { } ticket
        ? Results.Ok(ticket)
        : Results.NotFound(new { problem = $"There is no ticket #{invoice}." }));

// The settings every till shares. Machine settings - printer, server address, till name -
// are deliberately not here: they are true of a computer, not of a business.
app.MapGet("/settings", () => Results.Ok(ShopData.Settings()));

// The shop's own filing, for a till filling in a product it has just scanned.
app.MapGet("/categories", () => Results.Ok(ShopData.Categories()));
app.MapGet("/suppliers/names", () => Results.Ok(ShopData.Suppliers()));

// ================================================================ the back office
//
// Every one of these runs on this machine, under the identity the caller's token names, and
// calls the repository the shop's own back office calls. A remote screen can therefore do
// exactly what the person signed in at it is allowed to do, and nothing else.

// ---------------------------------------------------------------- products
app.MapGet("/products", (HttpRequest r, string? search, int? categoryId, bool? includeInactive) =>
    Authorised.Do(r, () => ShopBusinessApi.Products(search, categoryId, includeInactive == true)));

app.MapGet("/products/recent", (HttpRequest r) => Authorised.Do(r, ShopBusinessApi.RecentProducts));
app.MapGet("/products/low", (HttpRequest r) => Authorised.Do(r, ShopBusinessApi.LowStock));
app.MapGet("/products/out", (HttpRequest r) => Authorised.Do(r, ShopBusinessApi.OutOfStock));

app.MapGet("/products/expiring", (HttpRequest r, int withinDays) =>
    Authorised.Do(r, () => ShopBusinessApi.Expiring(withinDays)));

app.MapGet("/products/barcode-taken", (HttpRequest r, string barcode, int exceptId) =>
    Authorised.Do(r, () => ShopBusinessApi.BarcodeTaken(barcode, exceptId)));

app.MapGet("/products/barcode/{barcode}", (HttpRequest r, string barcode) =>
    Authorised.Do(r, () => ShopBusinessApi.ProductByBarcode(barcode)));

app.MapGet("/products/{id:int}", (HttpRequest r, int id) =>
    Authorised.Do(r, () => ShopBusinessApi.Product(id)));

app.MapPost("/products", (HttpRequest r, SaveProduct asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.CreateProduct(asked), s => s.Ok));

app.MapPut("/products/{id:int}", (HttpRequest r, int id, SaveProduct asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.UpdateProduct(asked), s => s.Ok));

app.MapPut("/products/{id:int}/active", (HttpRequest r, int id, SetProductActive asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.SetProductActive(id, asked.Active), s => s.Ok));

app.MapPost("/products/{id:int}/deliveries", (HttpRequest r, int id, ReceiveStock asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.ReceiveStock(id, asked), s => s.Ok));

// ---------------------------------------------------------------- the shelves
app.MapGet("/inventory/movements", (HttpRequest r, DateTime? from, DateTime? to, int? productId) =>
    Authorised.Do(r, () => ShopBusinessApi.Movements(ShopBusinessApi.Span(from, to), productId)));

app.MapGet("/inventory/losses", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.Losses(DateRange.Custom(from, to))));

app.MapPost("/inventory/{id:int}/count", (HttpRequest r, int id, CountShelf asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.CountShelf(id, asked), s => s.Ok));

app.MapPost("/inventory/{id:int}/adjustments", (HttpRequest r, int id, AdjustStock asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.AdjustStock(id, asked), s => s.Ok));

// ---------------------------------------------------------------- suppliers
app.MapGet("/suppliers", (HttpRequest r, bool? includeInactive, string? search) =>
    Authorised.Do(r, () => ShopBusinessApi.Suppliers(includeInactive == true, search)));

app.MapPost("/suppliers", (HttpRequest r, Supplier asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.CreateSupplier(asked), s => s.Ok));

app.MapPut("/suppliers/{id:int}", (HttpRequest r, int id, Supplier asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.UpdateSupplier(asked), s => s.Ok));

app.MapPut("/suppliers/{id:int}/active", (HttpRequest r, int id, SetActive asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.SetSupplierActive(id, asked.Active), s => s.Ok));

app.MapDelete("/suppliers/{id:int}", (HttpRequest r, int id) =>
    Authorised.Answering(r, () => ShopBusinessApi.DeleteSupplier(id), s => s.Ok));

app.MapGet("/suppliers/{id:int}/goods", (HttpRequest r, int id) =>
    Authorised.Do(r, () => ShopBusinessApi.SupplierGoods(id)));

app.MapGet("/suppliers/deliveries", (HttpRequest r, DateTime? from, DateTime? to, int? supplierId) =>
    Authorised.Do(r, () => ShopBusinessApi.Deliveries(ShopBusinessApi.Span(from, to), supplierId)));

app.MapGet("/suppliers/deliveries/{id:int}/lines", (HttpRequest r, int id) =>
    Authorised.Do(r, () => ShopBusinessApi.DeliveryLines(id)));

app.MapPost("/suppliers/{id:int}/deliveries", (HttpRequest r, int id, RecordDelivery asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.RecordDelivery(asked), s => s.Ok));

app.MapPost("/suppliers/deliveries/{id:int}/cancel", (HttpRequest r, int id, Reason why) =>
    Authorised.Answering(r, () => ShopBusinessApi.CancelDelivery(id, why.Text), s => s.Ok));

app.MapGet("/suppliers/payments", (HttpRequest r, DateTime? from, DateTime? to, int? supplierId) =>
    Authorised.Do(r, () => ShopBusinessApi.SupplierPayments(ShopBusinessApi.Span(from, to), supplierId)));

app.MapPost("/suppliers/{id:int}/payments", (HttpRequest r, int id, PaySupplier asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.PaySupplier(id, asked), s => s.Ok));

// ---------------------------------------------------------------- expenses
app.MapGet("/expenses", (HttpRequest r, DateTime? from, DateTime? to, int? categoryId, string? search) =>
    Authorised.Do(r, () => ShopBusinessApi.Expenses(ShopBusinessApi.Span(from, to), categoryId, search)));

app.MapPost("/expenses", (HttpRequest r, Expense asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.CreateExpense(asked), s => s.Ok));

app.MapPut("/expenses/{id:int}", (HttpRequest r, int id, Expense asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.UpdateExpense(asked), s => s.Ok));

app.MapPost("/expenses/{id:int}/void", (HttpRequest r, int id, Reason why) =>
    Authorised.Answering(r, () => ShopBusinessApi.VoidExpense(id, why.Text), s => s.Ok));

app.MapGet("/expenses/by-category", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.ExpensesByCategory(DateRange.Custom(from, to))));

app.MapGet("/expenses/total", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.ExpenseTotal(DateRange.Custom(from, to))));

app.MapGet("/expenses/categories", (HttpRequest r) => Authorised.Do(r, ShopBusinessApi.ExpenseCategories));

app.MapPost("/expenses/categories", (HttpRequest r, Named asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.AddExpenseCategory(asked.Name), s => s.Ok));

// ---------------------------------------------------------------- employees
app.MapGet("/employees", (HttpRequest r, bool? includeInactive) =>
    Authorised.Do(r, () => ShopBusinessApi.Employees(includeInactive == true)));

app.MapPost("/employees", (HttpRequest r, Worker asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.CreateEmployee(asked), s => s.Ok));

app.MapPut("/employees/{id:int}", (HttpRequest r, int id, Worker asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.UpdateEmployee(asked), s => s.Ok));

app.MapPut("/employees/{id:int}/active", (HttpRequest r, int id, SetActive asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.SetEmployeeActive(id, asked.Active), s => s.Ok));

app.MapPut("/employees/{id:int}/password", (HttpRequest r, int id, Secret asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.SetEmployeePassword(id, asked.Value), s => s.Ok));

app.MapGet("/employees/salaries", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.Salaries(DateRange.Custom(from, to))));

app.MapGet("/employees/paid", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.SalariesPaidIn(DateRange.Custom(from, to))));

app.MapPost("/employees/{id:int}/salary-payments", (HttpRequest r, int id, PaySalaryNow asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.PaySalary(id, asked), s => s.Ok));

// ---------------------------------------------------------------- sales history
app.MapGet("/sales", (HttpRequest r, DateTime? from, DateTime? to, string? search, int? workerId,
                      string? method, int? productId, int? categoryId) =>
    Authorised.Do(r, () => ShopBusinessApi.Sales(
        ShopBusinessApi.Span(from, to), search, workerId,
        Enum.TryParse<PaymentMethod>(method, out var how) ? how : null,
        productId, categoryId)));

app.MapGet("/sales/products", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.ProductPerformance(DateRange.Custom(from, to))));

app.MapGet("/sales/cashiers", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.WhoSoldIn(DateRange.Custom(from, to))));

app.MapGet("/sales/{invoice:int}", (HttpRequest r, int invoice) =>
    Authorised.Do(r, () => ShopBusinessApi.Sale(invoice)));

app.MapPost("/sales/{invoice:int}/refunds", (HttpRequest r, int invoice, RefundSale asked) =>
    Authorised.Answering(r, () => ShopBusinessApi.RefundSale(invoice, asked), s => s.Ok));

app.MapPost("/sales/{invoice:int}/cancel", (HttpRequest r, int invoice, Reason why) =>
    Authorised.Answering(r, () => ShopBusinessApi.CancelSale(invoice, why.Text), s => s.Ok));

// ---------------------------------------------------------------- activity
app.MapGet("/activity", (HttpRequest r, DateTime? from, DateTime? to, string? search, int? limit) =>
    Authorised.Do(r, () => ShopBusinessApi.Activity(ShopBusinessApi.Span(from, to), search, limit ?? 300)));

// ---------------------------------------------------------------- the figures
app.MapGet("/reports/money", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.Money(DateRange.Custom(from, to))));

app.MapGet("/reports/series", (HttpRequest r, DateTime from, DateTime to, string kind) =>
    Authorised.Do(r, () => ShopBusinessApi.Series(
        DateRange.Custom(from, to),
        Enum.TryParse<SeriesKind>(kind, out var which) ? which : SeriesKind.Revenue)));

app.MapGet("/reports/alerts", (HttpRequest r) => Authorised.Do(r, ShopBusinessApi.Alerts));

app.MapGet("/reports/losses", (HttpRequest r, DateTime from, DateTime to) =>
    Authorised.Do(r, () => ShopBusinessApi.Losses(DateRange.Custom(from, to))));

// ---------------------------------------------------------------- who is allowed in
//
// Both checks run against this machine's own database, and what comes back is a token that
// means nothing anywhere else. Nothing here is ever written to the log - not the token, and
// certainly not the password that earned it.

app.MapPost("/auth/owner/signin", (OwnerSignIn who) =>
{
    var said = ShopAuthApi.OwnerSignIn(who.Password);
    Note(said.Ok ? "the owner signed in" : "an owner sign-in was refused");
    return said.Ok ? Results.Ok(said) : Results.Json(said, statusCode: 401);
});

app.MapPost("/auth/staff/signin", (SignInRequest who) =>
{
    var said = ShopAuthApi.StaffSignIn(who.WorkerId, who.Password);
    Note(said.Ok ? $"{said.Name} signed in" : "a staff sign-in was refused");
    return said.Ok ? Results.Ok(said) : Results.Json(said, statusCode: 401);
});

app.MapPost("/auth/signout", (HttpRequest request) =>
{
    ShopTokens.Revoke(request.Headers[Api.TokenHeader].ToString());
    return Results.Ok(new Answered(true));
});

app.MapGet("/auth/whoami", (HttpRequest request) =>
    Results.Ok(ShopAuthApi.Whoami(request.Headers[Api.TokenHeader].ToString())));

// ---------------------------------------------------------------- categories

// The back office's own view of them, hidden ones included, which is what its list needs.
app.MapGet("/categories/all", (HttpRequest request, bool? includeInactive) =>
    Authorised.Do(request, () => ShopCategoriesApi.List(includeInactive == true)));

app.MapPost("/categories", (HttpRequest request, NewCategory asked) =>
    Authorised.Answering(request, () => ShopCategoriesApi.Create(asked), s => s.Ok, 400));

app.MapPut("/categories/{id:int}", (HttpRequest request, int id, RenameCategory asked) =>
    Authorised.Answering(request, () => ShopCategoriesApi.Rename(id, asked), s => s.Ok, 400));

app.MapPut("/categories/{id:int}/active", (HttpRequest request, int id, SetCategoryActive asked) =>
    Authorised.Answering(request, () => ShopCategoriesApi.SetActive(id, asked), s => s.Ok));

app.MapDelete("/categories/{id:int}", (HttpRequest request, int id) =>
    Authorised.Answering(request, () => ShopCategoriesApi.Delete(id), s => s.Ok));

// ---------------------------------------------------------------- who is allowed in


// ---------------------------------------------------------------- the pictures

// A product's photo. Business data: a shop that has photographed its shelves has done work,
// and the work belongs with the shop rather than on whichever counter took the picture.
app.MapGet("/products/{id:int}/photo", (int id) =>
    ShopData.Photo(id) is { } picture
        ? Results.File(picture.Bytes, picture.Type)
        : Results.NotFound());

app.MapGet("/categories/{id:int}/photo", (int id) =>
    ShopData.CategoryPhoto(id) is { } picture
        ? Results.File(picture.Bytes, picture.Type)
        : Results.NotFound());

// ---------------------------------------------------------------- is the shop up

// Asked by anything that wants to know whether the shop is answering before it commits to
// needing it: a till reconnecting, a person setting a machine up, a monitor on the shelf. It
// touches the database rather than only the web server, because a server that is listening
// over a database it cannot open is not healthy in any way that matters to a shop.
app.MapGet("/health", () =>
{
    var health = ShopTill.Health(serverId, out var well);
    if (!well) Note("HEALTH failed: the shop's database could not be opened");
    return well ? Results.Ok(health) : Results.Json(health, statusCode: 503);
});

// ---------------------------------------------------------------- a copy of the books

// A backup taken while the shop is trading.
//
// VACUUM INTO, not a file copy: under write-ahead logging the database is two files that only
// agree at a checkpoint, so copying the .db alone can produce something that opens and is
// missing the last hour of sales. SQLite writes a single consistent file here, from inside its
// own locking, whatever the tills are doing at the time.
//
// Restoring is the plainest thing in the app: stop the server, put the file where
// Database.Path says, start it again.
app.MapPost("/backup", () =>
{
    var (ok, file, bytes, problem) = ShopTill.Backup();
    Note(ok ? $"backup written to {file} ({bytes / 1024} KB)" : "backup failed: " + problem);

    return ok
        ? Results.Ok(new { ok = true, file, bytes })
        : Results.Json(new { ok = false, problem }, statusCode: 500);
});

// ---------------------------------------------------------------- a sale, made here

// The cashier's machine asks for this and waits. Everything that makes a sale a sale happens
// inside one transaction on this machine — the ticket, its lines, the stock coming off the
// shelf, the movements that record it — and the answer is the invoice number or the reason
// there is not one. A till never writes any of it.
app.MapPost("/checkout", (SaleUpload sale) =>
{
    var done = ShopTill.Checkout(sale, out var status);

    Note(done.Ok
        ? $"sale #{done.InvoiceNumber} for {sale.Total:0.00} from {sale.WorkerName}"
          + (done.AlreadyHad ? " (a repeat, already on the books)" : string.Empty)
        : $"checkout refused: {done.Problem}");

    return status == 200 ? Results.Ok(done) : Results.Json(done, statusCode: status);
});

// ---------------------------------------------------------------- sales coming in

app.MapPost("/sales", (SaleBatch batch) =>
    Results.Ok(ShopTill.Accept(batch, (reference, error) =>
        app.Logger.LogError(error, "Rejected sale {Reference} from a till", reference))));

// Says where it is, in words the person who has to type it into a till can use. A server
// that starts silently leaves them reading Kestrel's console output for an IP address.
foreach (var address in LocalAddresses())
{
    app.Logger.LogInformation("Tills should be pointed at https://{Address}:5000", address);
    Note($"tills should be pointed at https://{address}:5000");
}

Note($"database {MarketPos.Data.Database.Path}");
Note("running");

// Anything that stops this from starting stops the shop's tills, and a console window that
// closes as fast as it opened tells whoever double-clicked it nothing at all. So the reason is
// said in words, and the window is held open until it has been read.
try
{
    app.Run();
}
catch (Exception problem)
{
    Note("COULD NOT START " + problem);

    Console.WriteLine();
    Console.WriteLine("The shop server could not start.");
    Console.WriteLine();

    // Far and away the likeliest one: the all-in-one MarketPos.exe is open on this machine and
    // has taken the port, because it is the till and the server in one program.
    if (problem is IOException or System.Net.Sockets.SocketException)
    {
        Console.WriteLine("Something else on this machine is already using port 5000.");
        Console.WriteLine("It is probably MarketPos.exe — close it, start this server, then");
        Console.WriteLine("open it again. It will leave the port alone and use this server.");
    }
    else
    {
        Console.WriteLine(problem.Message);
    }

    Console.WriteLine();
    Console.WriteLine("Press any key to close.");

    // Only when somebody is looking. Started from a shortcut there is a console to read this
    // in; started by the machine at boot there is nobody, and waiting for a key would be a
    // server that never comes back after a power cut.
    if (!Console.IsInputRedirected)
    {
        try { Console.ReadKey(true); } catch { /* no console: nothing to wait for */ }
    }

    return 1;
}

return 0;

/// <summary>This machine's addresses on the shop's own network.</summary>
static List<string> LocalAddresses() =>
    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !System.Net.IPAddress.IsLoopback(a))
        .Select(a => a.ToString())
        .ToList();

// ---------------------------------------------------------------- helpers



/// <summary>
/// A fingerprint of the catalogue as the till would see it. Cheap to compute and changes on
/// anything a till cares about — a new product, a price, a name, stock moving.
/// </summary>
static string Stamp(IEnumerable<CatalogItem> items)
{
    var hash = new System.Text.StringBuilder();
    foreach (var i in items)
    {
        hash.Append(i.Id).Append(':')
            .Append(i.Barcode).Append(':')
            .Append(i.Name).Append(':')
            .Append(i.Price.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(i.Stock.ToString(CultureInfo.InvariantCulture)).Append(';');
    }

    var bytes = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(hash.ToString()));

    return Convert.ToHexString(bytes)[..16];
}
