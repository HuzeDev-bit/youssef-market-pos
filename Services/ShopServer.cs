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

            app.MapGet("/catalog", (string? since) =>
            {
                var items = StockRepository.List()
                    .Where(p => p.ShowInPos)
                    .Select(p => new CatalogItem(
                        p.Id, p.Barcode, p.Name, p.Category, p.Price, p.TaxRate, p.Unit.ToString(), p.Stock))
                    .ToList();

                var stamp = Stamp(items);

                if (!string.IsNullOrEmpty(since) && since == stamp)
                    return Results.Ok(new CatalogPage(stamp, Array.Empty<CatalogItem>(), Complete: false));

                return Results.Ok(new CatalogPage(stamp, items, Complete: true));
            });

            app.MapPost("/sales", (SaleBatch batch) =>
            {
                var accepted = new List<SaleAccepted>();
                var rejected = new List<string>();

                foreach (var sale in batch.Sales)
                {
                    try
                    {
                        accepted.Add(Record(sale));
                    }
                    catch
                    {
                        rejected.Add(sale.TillReference);
                    }
                }

                return Results.Ok(new SaleBatchResult(accepted, rejected));
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

    private static SaleAccepted Record(SaleUpload upload)
    {
        var catalogue = StockRepository.List(includeInactive: true);
        var byBarcode = catalogue.Where(p => p.Barcode.Length > 0)
                                 .ToDictionary(p => p.Barcode, StringComparer.Ordinal);
        var byId = catalogue.ToDictionary(p => p.Id);

        var lines = upload.Lines.Select(l =>
        {
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

    private static bool SeenBefore(string reference)
    {
        if (string.IsNullOrEmpty(reference)) return false;

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sales WHERE till_reference = $ref;";
        command.Parameters.AddWithValue("$ref", reference);
        return command.ExecuteScalar() is not null;
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
