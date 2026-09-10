using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Link;

/// <summary>What the app needs to do with the shop's products and its shelves.</summary>
public interface IStockService
{
    IReadOnlyList<StockItem> List(string? search = null, int? categoryId = null,
                                  bool includeInactive = false);

    StockItem? Find(int id);

    StockItem? FindByBarcode(string barcode);

    IReadOnlyList<StockItem> RecentlyAdded();

    IReadOnlyList<StockItem> LowStock();

    IReadOnlyList<StockItem> OutOfStock();

    IReadOnlyList<StockItem> Expiring(int withinDays);

    bool BarcodeTaken(string barcode, int exceptId = 0);

    int Create(StockItem item, decimal openingStock);

    void Update(StockItem item);

    void SetActive(int id, string name, bool active);

    void ReceiveAtTill(int id, decimal quantity, decimal? cost, decimal? price, DateTime? expiresOn);

    // ---- the shelves themselves ----

    IReadOnlyList<StockMovement> Movements(DateRange? range = null, int? productId = null);

    IReadOnlyList<(StockReason Reason, decimal Quantity, decimal Value)> LossesByReason(DateRange range);

    /// <summary>Counts the shelf and writes the difference. The shop refuses an impossible one.</summary>
    void SetCount(int id, string name, decimal counted, string note);

    /// <summary>Moves stock by a delta, with a reason. The shop refuses more than is there.</summary>
    void Move(int id, decimal delta, StockReason reason, string reference, string note, decimal? unitCost);
}

/// <summary>The shop's stock, on the machine that owns it. The repositories, unchanged.</summary>
public sealed class LocalStock : IStockService
{
    public IReadOnlyList<StockItem> List(string? search = null, int? categoryId = null,
                                         bool includeInactive = false) =>
        StockRepository.List(search: search, categoryId: categoryId, includeInactive: includeInactive);

    public StockItem? Find(int id) => StockRepository.Find(id);

    public StockItem? FindByBarcode(string barcode) => StockRepository.FindByBarcode(barcode);

    public IReadOnlyList<StockItem> RecentlyAdded() => StockRepository.RecentlyAdded();

    public IReadOnlyList<StockItem> LowStock() => StockRepository.LowStock();

    public IReadOnlyList<StockItem> OutOfStock() => StockRepository.OutOfStock();

    public IReadOnlyList<StockItem> Expiring(int withinDays) => StockRepository.Expiring(withinDays);

    public bool BarcodeTaken(string barcode, int exceptId = 0) =>
        StockRepository.BarcodeTaken(barcode, exceptId);

    public int Create(StockItem item, decimal openingStock) =>
        StockRepository.Create(item, openingStock);

    public void Update(StockItem item) => StockRepository.Update(item);

    public void SetActive(int id, string name, bool active) =>
        StockRepository.SetActive(id, name, active);

    public void ReceiveAtTill(int id, decimal quantity, decimal? cost, decimal? price, DateTime? expiresOn) =>
        StockRepository.ReceiveAtTill(id, quantity, cost, price, expiresOn);

    public IReadOnlyList<StockMovement> Movements(DateRange? range = null, int? productId = null) =>
        InventoryRepository.ListMovements(range, productId);

    public IReadOnlyList<(StockReason Reason, decimal Quantity, decimal Value)> LossesByReason(DateRange range) =>
        InventoryRepository.LossesByReason(range);

    public void SetCount(int id, string name, decimal counted, string note) =>
        InventoryRepository.SetCount(id, name, counted, note);

    public void Move(int id, decimal delta, StockReason reason, string reference, string note, decimal? unitCost) =>
        InventoryRepository.Move(id, delta, reason, reference: reference, note: note, unitCost: unitCost);
}

/// <summary>
/// The shop's stock, asked for over the wire.
///
/// Every write is a request the shop may refuse — a cashier without the right to manage stock,
/// a count that would take a shelf below nothing, a barcode another product already carries.
/// The refusal is the shop's, in its own words, and arrives as an exception so a screen cannot
/// mistake it for success.
/// </summary>
public sealed class RemoteStock : IStockService
{
    public IReadOnlyList<StockItem> List(string? search = null, int? categoryId = null,
                                         bool includeInactive = false)
    {
        var query = $"products?includeInactive={includeInactive}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        if (categoryId is { } id) query += $"&categoryId={id}";

        return Api.Get<List<StockItem>>(query) ?? new List<StockItem>();
    }

    public StockItem? Find(int id) => Api.Get<StockItem>($"products/{id}");

    public StockItem? FindByBarcode(string barcode) =>
        Api.Get<StockItem>($"products/barcode/{Uri.EscapeDataString(barcode)}");

    public IReadOnlyList<StockItem> RecentlyAdded() =>
        Api.Get<List<StockItem>>("products/recent") ?? new List<StockItem>();

    public IReadOnlyList<StockItem> LowStock() =>
        Api.Get<List<StockItem>>("products/low") ?? new List<StockItem>();

    public IReadOnlyList<StockItem> OutOfStock() =>
        Api.Get<List<StockItem>>("products/out") ?? new List<StockItem>();

    public IReadOnlyList<StockItem> Expiring(int withinDays) =>
        Api.Get<List<StockItem>>($"products/expiring?withinDays={withinDays}") ?? new List<StockItem>();

    public bool BarcodeTaken(string barcode, int exceptId = 0) =>
        Api.Get<Answered>($"products/barcode-taken?barcode={Uri.EscapeDataString(barcode)}&exceptId={exceptId}")
           ?.Ok ?? false;

    public int Create(StockItem item, decimal openingStock) =>
        Saved(Api.Post<StockSaved>("products", new SaveProduct(item, openingStock))).Id;

    public void Update(StockItem item) =>
        Saved(Api.Put<StockSaved>($"products/{item.Id}", new SaveProduct(item, 0m)));

    public void SetActive(int id, string name, bool active) =>
        Saved(Api.Put<StockSaved>($"products/{id}/active", new SetProductActive(active)));

    public void ReceiveAtTill(int id, decimal quantity, decimal? cost, decimal? price, DateTime? expiresOn) =>
        Saved(Api.Post<StockSaved>($"products/{id}/deliveries",
                                   new ReceiveStock(quantity, cost, price, expiresOn)));

    public IReadOnlyList<StockMovement> Movements(DateRange? range = null, int? productId = null)
    {
        var query = "inventory/movements?";
        if (range is { } r) query += $"from={r.From:O}&to={r.To:O}&";
        if (productId is { } id) query += $"productId={id}";

        return Api.Get<List<StockMovement>>(query) ?? new List<StockMovement>();
    }

    public IReadOnlyList<(StockReason Reason, decimal Quantity, decimal Value)> LossesByReason(DateRange range) =>
        (Api.Get<List<LossLine>>($"inventory/losses?from={range.From:O}&to={range.To:O}")
         ?? new List<LossLine>())
        .Select(l => (l.Reason, l.Quantity, l.Value))
        .ToList();

    public void SetCount(int id, string name, decimal counted, string note) =>
        Saved(Api.Post<StockSaved>($"inventory/{id}/count", new CountShelf(counted, note)));

    public void Move(int id, decimal delta, StockReason reason, string reference, string note, decimal? unitCost) =>
        Saved(Api.Post<StockSaved>($"inventory/{id}/adjustments",
                                   new AdjustStock(delta, reason.ToString(), reference, note, unitCost)));

    /// <summary>
    /// Turns the shop's refusal back into the exception the screens already handle.
    ///
    /// The back office was written against repositories that throw when the shop says no —
    /// NotEnoughStockException for a shelf that cannot cover it, UnauthorizedAccessException
    /// for somebody who may not. Those pages catch those types, so a refusal that arrived as a
    /// polite object would slip past every one of them and look like success.
    /// </summary>
    private static StockSaved Saved(StockSaved? said)
    {
        if (said is null) throw new ShopUnreachable("The shop did not answer.");
        if (said.Ok) return said;

        throw said.Refusal switch
        {
            nameof(NotEnoughStockException) => new NotEnoughStockException(said.Problem),
            nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(said.Problem),
            _ => new InvalidOperationException(said.Problem),
        };
    }
}

// ---------------------------------------------------------------- what crosses the wire

public sealed record SaveProduct(StockItem Item, decimal OpeningStock);

public sealed record SetProductActive(bool Active);

public sealed record ReceiveStock(decimal Quantity, decimal? Cost, decimal? Price, DateTime? ExpiresOn);

public sealed record CountShelf(decimal Counted, string Note);

public sealed record AdjustStock(decimal Delta, string Reason, string Reference, string Note, decimal? UnitCost);

public sealed record LossLine(StockReason Reason, decimal Quantity, decimal Value);

/// <summary>A plain yes or no from the shop.</summary>
public sealed record Answered(bool Ok);

/// <summary>What the shop made of a write: the row, or the refusal and what kind it was.</summary>
public sealed record StockSaved(bool Ok, int Id, string Problem, string Refusal);
