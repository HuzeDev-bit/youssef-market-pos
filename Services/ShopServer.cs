using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Link;

namespace MarketPos.Services;

/// <summary>
/// Embedded back-office server running directly inside the application process.
/// Listens on port 5000, allowing other tills on the local network or local diagnostics
/// to communicate with the shop's catalog and database.
/// </summary>
public static class ShopServer
{
    private static WebApplication? _app;

    public static void Start()
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://0.0.0.0:5000");

            builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
                p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

            var app = builder.Build();
            app.UseCors();

            var serverId = Environment.MachineName;

            app.MapGet("/hello", () => new Hello(
                AppSettings.Current.BusinessName, Contracts.Version, serverId, DateTime.Now));

            app.MapGet("/staff", () => WorkerRepository.ForSync()
                .Select(w => new StaffMember(w.Id, w.Name, w.Role, w.Hash, w.Salt, w.IsActive))
                .ToList());

            app.MapPost("/signin", (SignInRequest request) =>
            {
                var worker = WorkerRepository.SignIn(request.WorkerId, request.Password);
                return worker is null
                    ? Results.Unauthorized()
                    : Results.Ok(new SignedIn(worker.Id, worker.Name, worker.Role.ToString()));
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
                return said.Ok ? Results.Ok(said) : Results.Json(said, statusCode: 401);
            });

            app.MapPost("/auth/staff/signin", (SignInRequest who) =>
            {
                var said = ShopAuthApi.StaffSignIn(who.WorkerId, who.Password);
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
            app.MapGet("/categories/{id:int}/photo", (int id) =>
                ShopData.CategoryPhoto(id) is { } picture
                    ? Results.File(picture.Bytes, picture.Type)
                    : Results.NotFound());

            app.MapGet("/products/{id:int}/photo", (int id) =>
                ShopData.Photo(id) is { } picture
                    ? Results.File(picture.Bytes, picture.Type)
                    : Results.NotFound());

            app.MapGet("/catalog", (string? since) =>
            {
                var items = StockRepository.List()
                    .Where(p => p.ShowInPos)
                    .Select(p => new CatalogItem(
                        p.Id, p.Barcode, p.Name, p.Category, p.Price, p.TaxRate, p.Unit.ToString(), p.Stock,
                                 HasPhoto: !string.IsNullOrWhiteSpace(p.ImagePath)))
                    .ToList();

                var stamp = Stamp(items);

                if (!string.IsNullOrEmpty(since) && since == stamp)
                    return Results.Ok(new CatalogPage(stamp, Array.Empty<CatalogItem>(), Complete: false));

                return Results.Ok(new CatalogPage(stamp, items, Complete: true));
            });

                        app.MapPost("/sales", (SaleBatch batch) => Results.Ok(ShopTill.Accept(batch)));

            // ---------------------------------------------------------------- the till's own work
            //
            // A till pointed at this machine must be able to do everything it can do against the
            // standalone server, because from the counter they are the same shop.

            app.MapPost("/checkout", (SaleUpload sale) =>
            {
                var done = ShopTill.Checkout(sale, out var status);
                return status == 200 ? Results.Ok(done) : Results.Json(done, statusCode: status);
            });

            app.MapPost("/products/scanned", (NewProduct arriving) =>
            {
                var made = ShopTill.AddScanned(arriving, out var problem);
                return made is null ? Results.BadRequest(problem) : Results.Ok(made);
            });

            app.MapGet("/health", () =>
            {
                var health = ShopTill.Health(serverId, out var well);
                return well ? Results.Ok(health) : Results.Json(health, statusCode: 503);
            });

            app.MapPost("/backup", () =>
            {
                var (ok, file, bytes, problem) = ShopTill.Backup();
                return ok
                    ? Results.Ok(new { ok = true, file, bytes })
                    : Results.Json(new { ok = false, problem }, statusCode: 500);
            });

            _app = app;
            _ = app.RunAsync();
        }
        catch
        {
            // Port 5000 already in use (e.g. standalone server is running) — that's expected.
        }
    }

    public static void Stop()
    {
        try
        {
            if (_app != null)
            {
                var app = _app;
                _app = null;
                app.StopAsync().GetAwaiter().GetResult();
                app.DisposeAsync().GetAwaiter().GetResult();
            }
        }
        catch { }
    }

    
    
    private static string Stamp(IEnumerable<CatalogItem> items)
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
}
