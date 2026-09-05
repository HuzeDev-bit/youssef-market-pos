namespace MarketPos.Models;

// ============================== Enumerations ==============================

/// <summary>
/// What a person is allowed to do. Checked in <see cref="Services.Session"/> before any
/// repository call that matters, not only when drawing buttons — a hidden button is a hint,
/// not a lock.
/// </summary>
public enum WorkerRole
{
    Owner,
    Manager,
    Cashier,
    StockWorker,
}

public enum SalaryPeriod
{
    Monthly,
    Weekly,
    Daily,
}

/// <summary>Why a stock level changed. Every movement carries one; none is optional.</summary>
public enum StockReason
{
    SupplierPurchase,
    Sale,
    CustomerReturn,
    SupplierReturn,
    Damaged,
    Expired,
    Lost,
    Stolen,
    InternalUse,
    ManualCorrection,
    OpeningStock,
}

public enum PaymentStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
}

public enum SaleStatus
{
    Completed,
    PartlyRefunded,
    Refunded,
    Cancelled,
}

public enum StockStatus
{
    InStock,
    LowStock,
    OutOfStock,
}

public enum Recurrence
{
    None,
    Weekly,
    Monthly,
    Yearly,
}

// ================================ Records ================================

public sealed class Worker
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public WorkerRole Role { get; init; } = WorkerRole.Cashier;
    public DateTime StartedOn { get; init; } = DateTime.Today;
    public decimal Salary { get; init; }
    public SalaryPeriod SalaryPeriod { get; init; } = SalaryPeriod.Monthly;
    public bool IsActive { get; init; } = true;
    public string Note { get; init; } = string.Empty;
    public bool HasPin { get; init; }

    public string RoleLabel => Services.Loc.T(Role switch
    {
        WorkerRole.Owner => "Owner",
        WorkerRole.Manager => "Manager",
        WorkerRole.StockWorker => "Stock worker",
        _ => "Cashier",
    });

    /// <summary>First letter, for the avatar circle. Photographs of staff are not worth the trouble.</summary>
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    public string SalaryLabel => Salary <= 0m
        ? "—"
        : $"{Salary:N2} DH / {SalaryPeriod switch
        {
            Models.SalaryPeriod.Daily => "day",
            Models.SalaryPeriod.Weekly => "week",
            _ => "month",
        }}";
}

/// <summary>A worker's pay position for one period — due, paid and what is still owed.</summary>
public sealed class SalaryLedger
{
    public required Worker Worker { get; init; }
    public decimal Due { get; init; }
    public decimal Paid { get; init; }
    public decimal Remaining => Math.Max(0m, Due - Paid);

    public PaymentStatus Status =>
        Paid <= 0m ? PaymentStatus.Unpaid
        : Remaining > 0m ? PaymentStatus.PartiallyPaid
        : PaymentStatus.Paid;
}

public sealed class SalaryPayment
{
    public int Id { get; init; }
    public int WorkerId { get; init; }
    public string WorkerName { get; init; } = string.Empty;
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public decimal AmountDue { get; init; }
    public decimal AmountPaid { get; init; }
    public DateTime PaidOn { get; init; }
    public string Method { get; init; } = "Cash";
    public string Note { get; init; } = string.Empty;
}

public sealed class Supplier
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string Contact { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;

    /// <summary>Running balance, filled in by the repository when the list is loaded.</summary>
    public decimal TotalPurchased { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Owed => Math.Max(0m, TotalPurchased - TotalPaid);

    public bool IsOwed => Owed > 0m;

    /// <summary>A dash rather than a blank, so an empty column reads as "not known".</summary>
    public string PhoneLabel => Phone.Length > 0 ? Phone : "—";
}

/// <summary>
/// One product the shop buys from a supplier, summed over every delivery from them.
/// </summary>
public sealed class SupplierGoods
{
    public int ProductId { get; init; }
    public required string Name { get; init; }
    public decimal Quantity { get; init; }
    public decimal TotalCost { get; init; }
    public int Deliveries { get; init; }
    public DateTime LastBought { get; init; }

    /// <summary>What it cost per unit the last time, filled in by the repository.</summary>
    public decimal LastUnitCost { get; set; }

    /// <summary>Averaged over everything bought, which is what the total actually divides to.</summary>
    public decimal AverageUnitCost => Quantity <= 0m ? 0m : Math.Round(TotalCost / Quantity, 2);

    /// <summary>
    /// True when the last price paid is above the running average — the supplier has put it
    /// up, or the cheap deliveries are behind us. Either way it is worth a second look.
    /// </summary>
    public bool PriceWentUp => LastUnitCost > AverageUnitCost && AverageUnitCost > 0m;

    public string QuantityLabel => $"{Quantity:0.###}";
    public string LastCostLabel =>
        Services.Loc.T("{0} each", Services.Loc.Ltr($"{LastUnitCost:N2}"));

    public string LastBoughtLabel => LastBought == default
        ? string.Empty
        : Services.Loc.T("last {0}", LastBought.ToString("d MMM yyyy"));
}

public sealed class PurchaseLine
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public required string Name { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal LineTotal => Math.Round(Quantity * UnitCost, 2);

    /// <summary>
    /// What to sell it for from now on, or null to leave the shelf price alone.
    ///
    /// A delivery is when the owner finds out what they paid, so it is also when they decide
    /// what to charge. Setting it here saves walking to another screen with the invoice still
    /// in hand — and it is optional, because a price that has not moved should not have to be
    /// retyped to record a delivery.
    /// </summary>
    public decimal? SellPrice { get; init; }

    /// <summary>
    /// A product the shop does not stock yet, typed straight onto the delivery.
    ///
    /// Carried as a non-positive id rather than a flag so lines stay comparable by id, and
    /// each unentered product still gets its own key. The repository turns these into real
    /// products when the delivery is saved.
    /// </summary>
    public bool IsNew => ProductId <= 0;

    /// <summary>
    /// The code on the box, when a new product was scanned rather than typed. Null means the
    /// shop will give it an in-store code of its own.
    /// </summary>
    public string? Barcode { get; init; }

    /// <summary>What the shop makes on each one at that price. Null when no price was set.</summary>
    public decimal? Margin => SellPrice is { } price ? price - UnitCost : null;

    public string SellLabel => SellPrice is { } price ? $"{price:N2}" : "—";

    public string MarginLabel => Margin switch
    {
        null => string.Empty,
        <= 0m => "at a loss",
        var m when SellPrice > 0m => $"+{m:N2} ({m / SellPrice!.Value * 100m:0}%)",
        var m => $"+{m:N2}",
    };
}

public sealed class Purchase
{
    public int Id { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime PurchasedOn { get; init; }
    public DateTime? DueOn { get; init; }
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
    public string Method { get; init; } = "Cash";
    public string Note { get; init; } = string.Empty;
    public string Status { get; init; } = "Received";
    public bool Received { get; init; } = true;
    public List<PurchaseLine> Lines { get; init; } = new();

    public decimal Remaining => Math.Max(0m, Total - Paid);

    public PaymentStatus PaymentStatus =>
        Paid <= 0m ? PaymentStatus.Unpaid
        : Remaining > 0m ? PaymentStatus.PartiallyPaid
        : PaymentStatus.Paid;

    public bool IsOverdue => DueOn is { } due && Remaining > 0m && due.Date < DateTime.Today;
}

public sealed class SupplierPayment
{
    public int Id { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public int? PurchaseId { get; init; }
    public decimal Amount { get; init; }
    public DateTime PaidOn { get; init; }
    public string Method { get; init; } = "Cash";
    public string Note { get; init; } = string.Empty;
}

public sealed class Expense
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int? CategoryId { get; init; }
    public string Category { get; init; } = "Other";
    public decimal Amount { get; init; }
    public DateTime SpentOn { get; init; }
    public string Method { get; init; } = "Cash";
    public string Note { get; init; } = string.Empty;
    public string? ReceiptPath { get; init; }
    public Recurrence Recurring { get; init; } = Recurrence.None;
    public bool IsVoid { get; init; }

    /// <summary>True for a bill that comes back — the ones a shop has to cover before it earns.</summary>
    public bool Repeats => Recurring != Recurrence.None;

    public string RepeatLabel => Recurring switch
    {
        Recurrence.Weekly => "Weekly",
        Recurrence.Monthly => "Monthly",
        Recurrence.Yearly => "Yearly",
        _ => string.Empty,
    };
}

public sealed class StockMovement
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public DateTime MovedAt { get; init; }
    public StockReason Reason { get; init; }
    public decimal Quantity { get; init; }
    public decimal BeforeQty { get; init; }
    public decimal AfterQty { get; init; }
    public decimal UnitCost { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string WorkerName { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;

    public decimal Value => Math.Round(Math.Abs(Quantity) * UnitCost, 2);

    public string ReasonLabel => Reason switch
    {
        StockReason.SupplierPurchase => "Supplier purchase",
        StockReason.CustomerReturn => "Customer return",
        StockReason.SupplierReturn => "Supplier return",
        StockReason.InternalUse => "Internal use",
        StockReason.ManualCorrection => "Manual correction",
        StockReason.OpeningStock => "Opening stock",
        _ => Reason.ToString(),
    };

    /// <summary>True for the reasons that destroy value rather than move it — the loss report.</summary>
    public bool IsLoss => Reason is StockReason.Damaged or StockReason.Expired
                                 or StockReason.Lost or StockReason.Stolen
                                 or StockReason.InternalUse;
}

public sealed class Shift
{
    public int Id { get; init; }
    public int WorkerId { get; init; }
    public string WorkerName { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public decimal OpeningCash { get; init; }
    public decimal? ClosingCash { get; init; }
    public string Note { get; init; } = string.Empty;

    public int SaleCount { get; set; }
    public decimal Sales { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }

    public decimal ExpectedCash => OpeningCash + CashSales + CashIn - CashOut;
    public decimal? Difference => ClosingCash.HasValue ? ClosingCash.Value - ExpectedCash : null;
    public bool IsOpen => EndedAt is null;

    // Display shapes, so the XAML never has to work out what a null means.
    public string EndLabel => EndedAt is { } end ? end.ToString("HH:mm") : "still open";

    public string LengthLabel
    {
        get
        {
            var span = (EndedAt ?? DateTime.Now) - StartedAt;
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : $"{span.Minutes}m";
        }
    }

    public string CountedLabel => ClosingCash is { } counted ? $"{counted:N2} DH" : "—";

    public string DifferenceLabel => Difference is { } d
        ? $"{(d > 0 ? "+" : d < 0 ? "−" : string.Empty)}{Math.Abs(d):N2} DH"
        : "—";

    public bool IsShort => Difference is { } d && d < 0m;
    public bool IsExact => Difference == 0m;
}

public sealed class CashMovement
{
    public int Id { get; init; }
    public int? ShiftId { get; init; }
    public DateTime MovedAt { get; init; }
    public decimal Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public string WorkerName { get; init; } = string.Empty;
}

public sealed class ActivityEntry
{
    public int Id { get; init; }
    public DateTime HappenedAt { get; init; }
    public string WorkerName { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Entity { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    /// <summary>First letter of whoever did it, for the avatar circle.</summary>
    public string Initial => WorkerName.Length > 0 ? WorkerName[..1].ToUpperInvariant() : "?";

    /// <summary>One readable sentence, e.g. "Ahmed changed Coca-Cola stock from 25 to 30."</summary>
    public string Sentence
    {
        get
        {
            var who = string.IsNullOrWhiteSpace(WorkerName) ? "Someone" : WorkerName;
            var what = string.IsNullOrWhiteSpace(Detail) ? Action : Detail;

            // Only a change has a "from". Most entries carry a new value and no old one — the
            // detail already names it — and reading "from  to 250.00 DH" on all of those was
            // the sentence describing something that never happened.
            if (OldValue.Length > 0 && NewValue.Length > 0)
                return $"{who} {what}, from {OldValue} to {NewValue}.";
            if (OldValue.Length > 0)
                return $"{who} {what}, was {OldValue}.";

            return $"{who} {what}.";
        }
    }
}

/// <summary>A product row as the back office sees it — cost, stock and supplier included.</summary>
public sealed class StockItem
{
    public int Id { get; init; }
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int CategoryId { get; init; }
    public decimal Cost { get; init; }
    public decimal Price { get; init; }
    public decimal Stock { get; init; }
    public decimal MinStock { get; init; }
    public Unit Unit { get; init; } = Unit.Each;
    public decimal TaxRate { get; init; } = 0.20m;
    public string Shelf { get; init; } = string.Empty;
    public int? SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public DateTime? ExpiresOn { get; init; }
    public bool ShowInPos { get; init; } = true;
    public bool IsActive { get; init; } = true;

    /// <summary>When the product was first put into the shop.</summary>
    public DateTime CreatedAt { get; init; }
    public string? ImagePath { get; init; }

    public decimal StockValue => Math.Round(Stock * Cost, 2);
    public decimal RetailValue => Math.Round(Stock * Price, 2);
    public decimal Margin => Price - Cost;
    public decimal MarginPercent => Price <= 0m ? 0m : Math.Round((Price - Cost) / Price * 100m, 1);

    public StockStatus Status =>
        Stock <= 0m ? StockStatus.OutOfStock
        : Stock <= MinStock ? StockStatus.LowStock
        : StockStatus.InStock;

    public string StatusLabel => Status switch
    {
        StockStatus.OutOfStock => "Out of stock",
        StockStatus.LowStock => "Low stock",
        _ => "In stock",
    };

    /// <summary>Days until expiry, or null when the product does not carry a date.</summary>
    public int? DaysToExpiry => ExpiresOn is { } d ? (int)(d.Date - DateTime.Today).TotalDays : null;
    public bool IsExpiring => DaysToExpiry is >= 0 and <= 30;
    public bool IsExpired => DaysToExpiry is < 0;

    /// <summary>
    /// Whether the shop knows what it paid. Only counts when there is stock to value: a
    /// product with none on the shelf contributes nothing either way, and flagging it would
    /// bury the ones that actually make the total wrong.
    /// </summary>
    public bool HasCost => Cost > 0m || Stock <= 0m;

    /// <summary>
    /// How much is on the shelf, with the unit when it matters. "3" and "3 kg" are different
    /// facts, and a stock list that shows both as "3" is one a shopkeeper cannot count against.
    /// </summary>
    public string StockLabel => Unit == Unit.Kg
        ? $"{Stock:0.###} kg"
        : Stock.ToString("0.###");

    /// <summary>
    /// When it goes off, said the way it would be said out loud. A date alone makes the reader
    /// do the arithmetic that decides whether they need to act today.
    /// </summary>
    public string ExpiryLabel => DaysToExpiry switch
    {
        null => "—",
        < 0 and var d => $"{-d}d ago",
        0 => "Today",
        1 => "Tomorrow",
        <= 60 and var d => $"in {d}d",
        _ => ExpiresOn!.Value.ToString("d MMM yyyy"),
    };
}

public sealed class CategoryRow
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string Icon { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public int ProductCount { get; set; }

    /// <summary>File name of the category picture, empty when it has none.</summary>
    public string Image { get; init; } = string.Empty;

    /// <summary>Full path to the picture, or null — what the card binds to.</summary>
    public string? ImagePath => Services.CategoryImages.Find(Image);

    /// <summary>How much stock sits in this category, filled in by the page that needs it.</summary>
    public decimal StockValue { get; set; }
    public decimal RetailValue { get; set; }
    public int LowCount { get; set; }

    /// <summary>
    /// What to draw on the tile. Falls back to the category's initial, because a category
    /// with no icon is the normal case on a fresh shop and an empty square says nothing about
    /// which one it is.
    ///
    /// Trimmed to a single character here as well as on save: rows written before that rule
    /// existed still hold whole words, and a card is not the place to find that out.
    /// </summary>
    public string IconLabel
    {
        get
        {
            var source = Icon.Length > 0 ? Icon : Name;
            if (source.Length == 0) return "?";

            var walker = System.Globalization.StringInfo.GetTextElementEnumerator(source);
            if (!walker.MoveNext()) return "?";

            var glyph = (string)walker.Current;
            return Icon.Length > 0 ? glyph : glyph.ToUpperInvariant();
        }
    }

    public bool HasPicture => ImagePath is not null;

    /// <summary>What is in it.</summary>
    public string CountLabel => ProductCount switch
    {
        0 => "Nothing in it yet",
        1 => "1 product",
        _ => $"{ProductCount} products",
    };

    /// <summary>What that stock cost, blank when there is none to speak of.</summary>
    public string ValueLabel => StockValue <= 0m
        ? string.Empty
        : $"{StockValue:N2} {Services.AppSettings.Current.Currency}";

    /// <summary>Said only when there is something to say — silence is the good case.</summary>
    public string LowLabel => LowCount switch
    {
        0 => string.Empty,
        1 => "1 needs restocking",
        _ => $"{LowCount} need restocking",
    };
}

/// <summary>One alert on the notification page. Severity drives the colour, nothing else.</summary>
public sealed class Alert
{
    public required string Title { get; init; }
    public string Detail { get; init; } = string.Empty;
    public AlertLevel Level { get; init; } = AlertLevel.Info;
    public AdminPage? GoTo { get; init; }
}

public enum AlertLevel
{
    Info,
    Warning,
    Danger,
}
