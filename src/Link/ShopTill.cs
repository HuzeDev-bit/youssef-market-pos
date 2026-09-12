using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// What the shop does when a till asks it to do something.
///
/// <para>
/// <see cref="ShopData"/> is the shop answering questions; this is the shop acting on them — a
/// sale rung up, a product filled in at the counter, a copy of the books taken while the shop
/// is trading. Both are written once and used by both servers, the all-in-one and the standalone
/// one, so a till cannot be told two different things about the same shop depending on which of
/// them the owner happened to start.
/// </para>
///
/// <para>
/// They were not written once, to begin with. Each server carried its own copy of the sale
/// recording and its own idea of which paths a till should call, which is how the all-in-one
/// came to have no way to take a sale at all: a till pointed at the shop's own machine could
/// read the catalogue perfectly and then fail at the moment somebody tried to pay.
/// </para>
/// </summary>
public static class ShopTill
{
    /// <summary>
    /// A sale, made here.
    ///
    /// The cashier's machine asks for this and waits. Everything that makes a sale a sale
    /// happens inside one transaction on this machine — the ticket, its lines, the stock coming
    /// off the shelf, the movements that record it — and the answer is the invoice number or
    /// the reason there is not one. A till never writes any of it.
    /// </summary>
    public static CheckoutDone Checkout(SaleUpload sale, out int status)
    {
        if (sale.Lines.Count == 0)
        {
            status = 400;
            return new CheckoutDone(false, 0, false, "There is nothing in the basket.");
        }

        try
        {
            var done = Record(sale);
            status = 200;
            return new CheckoutDone(true, done.InvoiceNumber, done.AlreadyHad, string.Empty);
        }
        catch (NotEnoughStockException shortfall)
        {
            // The commonest way a checkout fails on a shop with two counters, and the one the
            // cashier can actually do something about.
            status = 409;
            return new CheckoutDone(false, 0, false, shortfall.Message);
        }
        catch (Exception problem)
        {
            status = 500;
            return new CheckoutDone(false, 0, false, problem.Message);
        }
    }

    /// <summary>
    /// Sales handed over in a batch by a till that has been away.
    ///
    /// One at a time, and a bad one does not stop the rest: a till that has been offline for a
    /// day may be handing over forty sales, and one of them being unsaveable must not hold the
    /// other thirty-nine hostage.
    /// </summary>
    public static SaleBatchResult Accept(SaleBatch batch, Action<string, Exception>? complain = null)
    {
        var accepted = new List<SaleAccepted>();
        var rejected = new List<string>();

        foreach (var sale in batch.Sales)
        {
            try
            {
                accepted.Add(Record(sale));
            }
            catch (Exception error)
            {
                complain?.Invoke(sale.TillReference, error);
                rejected.Add(sale.TillReference);
            }
        }

        return new SaleBatchResult(accepted, rejected);
    }

    /// <summary>
    /// A cashier scanned something the shop does not sell and filled its details in at the
    /// counter. It is created here, in the shop's own database, so that it exists for the back
    /// office, the stock list and every other till the moment it is saved — and so that the
    /// record of who added it and when is written in the one place that keeps such records.
    /// </summary>
    public static ProductAccepted? AddScanned(NewProduct arriving, out string problem)
    {
        problem = string.Empty;
        var barcode = (arriving.Barcode ?? string.Empty).Trim();

        if (arriving.Name.Trim().Length == 0)
        {
            problem = "A product needs a name.";
            return null;
        }

        // A barcode is optional — a product with nothing printed on it is saved without one,
        // and is found at the till by its picture. Only a barcode that is actually there can
        // clash. Two tills can scan the same unknown thing within a minute of each other, and
        // the second is not an error: the shop already has it, which is the answer that till
        // needs.
        if (barcode.Length > 0 && StockRepository.FindByBarcode(barcode) is { } already)
            return new ProductAccepted(already.Id, already.Barcode, already.Name, true);

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

        return new ProductAccepted(id, barcode, arriving.Name.Trim(), false);
    }

    /// <summary>
    /// Is the shop up.
    ///
    /// Asked by anything that wants to know whether the shop is answering before it commits to
    /// needing it: a till reconnecting, a person setting a machine up, a monitor on the shelf.
    /// It touches the database rather than only the web server, because a server that is
    /// listening over a database it cannot open is not healthy in any way that matters to a shop.
    /// </summary>
    public static Health Health(string serverId, out bool well)
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
            well = true;
            return new Health(
                "ok",
                AppSettings.Current.BusinessName,
                Contracts.Version,
                serverId,
                Database.Path,
                Count("SELECT COUNT(*) FROM products WHERE is_active = 1"),
                Count($"SELECT COUNT(*) FROM sales WHERE sold_at >= '{today}' AND is_voided = 0"),
                DateTime.Now);
        }
        catch
        {
            well = false;
            return new Health("failing", string.Empty, Contracts.Version, serverId,
                              Database.Path, 0, 0, DateTime.Now);
        }
    }

    /// <summary>
    /// A copy of the books, taken while the shop is trading.
    ///
    /// <para>
    /// VACUUM INTO, not a file copy: under write-ahead logging the database is two files that
    /// only agree at a checkpoint, so copying the .db alone can produce something that opens and
    /// is missing the last hour of sales. SQLite writes a single consistent file here, from
    /// inside its own locking, whatever the tills are doing at the time.
    /// </para>
    ///
    /// <para>
    /// Restoring is the plainest thing in the app: stop the server, put the file where
    /// <see cref="Database.Path"/> says, start it again.
    /// </para>
    /// </summary>
    public static (bool Ok, string File, long Bytes, string Problem) Backup()
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

            return (true, into, new System.IO.FileInfo(into).Length, string.Empty);
        }
        catch (Exception problem)
        {
            return (false, string.Empty, 0, problem.Message);
        }
    }

    // ---------------------------------------------------------------- putting a sale on the books

    /// <summary>
    /// One sale, written to the shop's books exactly as the till charged for it.
    ///
    /// The till sends what it charged, and that is what is stored: the customer paid that price,
    /// whatever the shelf says by the time the sale arrives. The product is looked up only to
    /// attach the sale to the right row and move the right stock — by barcode first, because the
    /// till's row id comes from a catalogue it may have been carrying for a day, and an id that
    /// no longer means the same product would take the stock off the wrong shelf.
    /// </summary>
    public static SaleAccepted Record(SaleUpload upload)
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

    /// <summary>True when this sale is already on the books — a retry, not a new sale.</summary>
    private static bool SeenBefore(string reference)
    {
        if (string.IsNullOrEmpty(reference)) return false;

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sales WHERE till_reference = $ref;";
        command.Parameters.AddWithValue("$ref", reference);
        return command.ExecuteScalar() is not null;
    }
}
