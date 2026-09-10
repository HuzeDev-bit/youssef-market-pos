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
            p.Id, p.Barcode, p.Name, p.Category, p.Price, p.TaxRate, p.Unit.ToString(), p.Stock))
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
app.MapPost("/products", (NewProduct arriving) =>
{
    var barcode = (arriving.Barcode ?? string.Empty).Trim();

    if (arriving.Name.Trim().Length == 0)
        return Results.BadRequest("A product needs a name.");

    // A barcode is optional — a product with nothing printed on it is saved without one, and
    // is found at the till by its picture. Only a barcode that is actually there can clash.
    if (barcode.Length > 0)
    {
        // Two tills can scan the same unknown thing within a minute of each other, and the
        // second is not an error: the shop already has it, which is the answer that till needs.
        var already = StockRepository.FindByBarcode(barcode);
        if (already is not null)
        {
            Note($"product {barcode} was already here as {already.Name}");
            return Results.Ok(new ProductAccepted(already.Id, already.Barcode, already.Name, true));
        }
    }

    var id = StockRepository.Create(new StockItem
    {
        Barcode = barcode,
        Name = arriving.Name.Trim(),
        Category = arriving.Category.Trim(),
        Price = arriving.Price,
        Cost = arriving.Cost,
        TaxRate = arriving.TaxRate,
        Unit = arriving.Unit == nameof(Unit.Kg) ? Unit.Kg : Unit.Each,
        MinStock = AppSettings.Current.DefaultLowStock,
        ShowInPos = true,
    }, openingStock: arriving.Stock);

    Note($"{arriving.AddedBy} added {arriving.Name} "
       + $"({(barcode.Length > 0 ? barcode : "no barcode")}) from a till");
    return Results.Ok(new ProductAccepted(id, barcode, arriving.Name.Trim(), false));
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
app.MapGet("/suppliers", () => Results.Ok(ShopData.Suppliers()));

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

// ---------------------------------------------------------------- is the shop up

// Asked by anything that wants to know whether the shop is answering before it commits to
// needing it: a till reconnecting, a person setting a machine up, a monitor on the shelf. It
// touches the database rather than only the web server, because a server that is listening
// over a database it cannot open is not healthy in any way that matters to a shop.
app.MapGet("/health", () =>
{
    try
    {
        using var connection = Database.Open();

        int Count(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        var today = DateTime.Today.ToString("O");
        return Results.Ok(new Health(
            "ok",
            AppSettings.Current.BusinessName,
            Contracts.Version,
            serverId,
            Database.Path,
            Count("SELECT COUNT(*) FROM products WHERE is_active = 1"),
            Count($"SELECT COUNT(*) FROM sales WHERE sold_at >= '{today}' AND is_voided = 0"),
            DateTime.Now));
    }
    catch (Exception problem)
    {
        Note("HEALTH failed: " + problem.Message);
        return Results.Json(new Health("failing", string.Empty, Contracts.Version, serverId,
                                       Database.Path, 0, 0, DateTime.Now),
                            statusCode: 503);
    }
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
    try
    {
        var folder = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Database.Path)!, "backups");
        System.IO.Directory.CreateDirectory(folder);

        var into = System.IO.Path.Combine(folder, $"marketpos-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $into;";
        command.Parameters.AddWithValue("$into", into);
        command.ExecuteNonQuery();

        var size = new System.IO.FileInfo(into).Length;
        Note($"backup written to {into} ({size / 1024} KB)");
        return Results.Ok(new { ok = true, file = into, bytes = size });
    }
    catch (Exception problem)
    {
        Note("backup failed: " + problem.Message);
        return Results.Json(new { ok = false, problem = problem.Message }, statusCode: 500);
    }
});

// ---------------------------------------------------------------- a sale, made here

// The cashier's machine asks for this and waits. Everything that makes a sale a sale happens
// inside one transaction on this machine — the ticket, its lines, the stock coming off the
// shelf, the movements that record it — and the answer is the invoice number or the reason
// there is not one. A till never writes any of it.
app.MapPost("/checkout", (SaleUpload sale) =>
{
    if (sale.Lines.Count == 0)
        return Results.BadRequest(new CheckoutDone(false, 0, false, "There is nothing in the basket."));

    try
    {
        var done = Record(sale);

        Note($"sale #{done.InvoiceNumber} for {sale.Total:0.00} from {sale.WorkerName}"
           + (done.AlreadyHad ? " (a repeat, already on the books)" : string.Empty));

        return Results.Ok(new CheckoutDone(true, done.InvoiceNumber, done.AlreadyHad, string.Empty));
    }
    catch (NotEnoughStockException shortfall)
    {
        // The commonest way a checkout fails on a shop with two counters, and the one the
        // cashier can actually do something about.
        Note("checkout refused: " + shortfall.Message);
        return Results.Json(new CheckoutDone(false, 0, false, shortfall.Message), statusCode: 409);
    }
    catch (Exception problem)
    {
        app.Logger.LogError(problem, "Checkout failed for {Reference}", sale.TillReference);
        Note("checkout failed: " + problem);
        return Results.Json(new CheckoutDone(false, 0, false, problem.Message), statusCode: 500);
    }
});

// ---------------------------------------------------------------- sales coming in

app.MapPost("/sales", (SaleBatch batch) =>
{
    var accepted = new List<SaleAccepted>();
    var rejected = new List<string>();

    // One at a time, and a bad one does not stop the rest: a till that has been offline for a
    // day may be handing over forty sales, and one of them being unsaveable must not hold the
    // other thirty-nine hostage.
    foreach (var sale in batch.Sales)
    {
        try
        {
            accepted.Add(Record(sale));
        }
        catch (Exception error)
        {
            app.Logger.LogError(error, "Rejected sale {Reference} from a till", sale.TillReference);
            rejected.Add(sale.TillReference);
        }
    }

    return Results.Ok(new SaleBatchResult(accepted, rejected));
});

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

SaleAccepted Record(SaleUpload upload)
{
    var catalogue = StockRepository.List(includeInactive: true);
    var byBarcode = catalogue.Where(p => p.Barcode.Length > 0)
                             .ToDictionary(p => p.Barcode, StringComparer.Ordinal);
    var byId = catalogue.ToDictionary(p => p.Id);

    var lines = upload.Lines.Select(l =>
    {
        // The till sends what it charged, and that is what is stored: the customer paid that
        // price, whatever the shelf says by the time the sale arrives. The product is looked
        // up only to attach the sale to the right row and move the right stock.
        //
        // By barcode first. The till's row id comes from a catalogue it may have been carrying
        // for a day, and an id that no longer means the same product would take the stock off
        // the wrong shelf. The barcode is what the shop itself calls the thing.
        var known = (l.Barcode.Length > 0 ? byBarcode.GetValueOrDefault(l.Barcode) : null)
                    ?? byId.GetValueOrDefault(l.ProductId);

        return new SaleItem(
            new Product
            {
                Id = known?.Id ?? 0,
                Barcode = l.Barcode,
                Name = l.Name,
                Price = l.UnitPrice,
                TaxRate = l.TaxRate,
                Unit = Enum.TryParse<Unit>(l.Unit, out var unit) ? unit : Unit.Each,
                Category = known?.Category ?? string.Empty,
            },
            l.Quantity);
    }).ToList();

    var before = SeenBefore(upload.TillReference);

    var invoice = SaleRepository.Save(
        lines,
        upload.GrossBeforeDiscount,
        Enum.TryParse<DiscountKind>(upload.DiscountKind, out var kind) ? kind : DiscountKind.None,
        upload.DiscountValue,
        upload.DiscountAmount,
        upload.Subtotal,
        upload.Tax,
        upload.Total,
        Enum.TryParse<PaymentMethod>(upload.PaymentMethod, out var method) ? method : PaymentMethod.Cash,
        upload.AmountTendered,
        new SaleOrigin(upload.SoldAt, upload.WorkerId, upload.WorkerName, upload.TillReference));

    return new SaleAccepted(upload.TillReference, invoice, before);
}

/// <summary>True when this sale is already on the books — a retry, not a new sale.</summary>
bool SeenBefore(string reference)
{
    if (string.IsNullOrEmpty(reference)) return false;

    using var connection = Database.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1 FROM sales WHERE till_reference = $ref;";
    command.Parameters.AddWithValue("$ref", reference);
    return command.ExecuteScalar() is not null;
}

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
