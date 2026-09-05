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

var builder = WebApplication.CreateBuilder(args);

// Listens on the shop's network, not just on this machine. Kestrel's default is localhost,
// which works perfectly on the developer's laptop and is invisible to every till in the shop —
// the failure looks like a broken cable and is a one-line setting. Overridable, so a shop that
// needs another port can still pass --urls.
if (!args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase))
    && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
{
    builder.WebHost.UseUrls("http://0.0.0.0:5000");
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
    app.Logger.LogInformation("Tills should be pointed at http://{Address}:5000", address);

app.Run();
return;

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
