using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using MarketPos.Data;
using MarketPos.Link;

namespace MarketPos.Services;

/// <summary>
/// The till's end of the shop's network.
///
/// One machine owns the books — the one running the back office and the server. Every other
/// till keeps a copy of the catalogue so it can sell, and hands its sales over afterwards.
///
/// The rule the whole design turns on: <b>the till never waits for the network to make a
/// sale</b>. A shop with a customer at the counter and a loose cable behind a fridge has to
/// keep trading, so a sale is written to this machine's own database first, queued, and
/// delivered when the server is there. That is why every sale carries a reference the till
/// minted itself: handing the same one over twice is how an afternoon's takings get counted
/// twice, and the reference is what lets the server recognise a repeat.
///
/// A shop with one computer never touches any of this. <see cref="IsConfigured"/> is false
/// until somebody types an address in Settings, and until then the till reads and writes its
/// own database exactly as it always did.
/// </summary>
public static class ShopLink
{
    private static readonly HttpClient Http = new()
    {
        // Short on purpose. This runs while somebody is standing at a counter; a request that
        // hangs for thirty seconds has already failed as far as the shop is concerned.
        Timeout = TimeSpan.FromSeconds(6),
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Where the back office answers. Empty means this machine works alone.</summary>
    public static string Address => AppSettings.Current.ServerAddress.Trim().TrimEnd('/');

    public static bool IsConfigured => Address.Length > 0;

    /// <summary>True after a successful exchange, false the moment one fails.</summary>
    public static bool IsOnline { get; private set; }

    /// <summary>The shop's name as the server gives it — proof the till is talking to the right one.</summary>
    public static string ShopName { get; private set; } = string.Empty;

    /// <summary>Why the last attempt failed, in words a shopkeeper can act on.</summary>
    public static string LastProblem { get; private set; } = string.Empty;

    public static DateTime LastSyncedAt { get; private set; }

    /// <summary>Raised whenever the state changes, so the till can redraw its indicator.</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// One line for the corner of the till. Deliberately about the shop rather than the
    /// network: "3 sales waiting" is something a shopkeeper can act on, "HTTP 503" is not.
    /// </summary>
    public static string Status
    {
        get
        {
            if (!IsConfigured) return string.Empty;

            var waiting = Waiting;
            if (IsOnline)
                return waiting == 0
                    ? Loc.T("Connected")
                    : $"{Loc.T("Connected")} · {waiting}";

            return waiting == 0
                ? Loc.T("Working offline")
                : $"{Loc.T("Working offline")} · {waiting}";
        }
    }

    /// <summary>Sales taken here that the server has not confirmed.</summary>
    public static int Waiting
    {
        get { try { return OutboxRepository.PendingCount(); } catch { return 0; } }
    }

    // ---------------------------------------------------------------- queueing

    /// <summary>
    /// Mints a reference no other till can produce. The machine's name is in it so two tills
    /// cannot collide, and the timestamp makes it readable when somebody has to trace a sale
    /// by hand.
    /// </summary>
    public static string NewReference() =>
        $"{AppSettings.Current.TillLabel}-{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString()[..6]}";

    /// <summary>
    /// Puts a sale in the queue. Called by <see cref="SaleRepository"/> as part of saving one,
    /// so a sale cannot be taken without being queued — a shop where that could come apart is
    /// a shop that loses money quietly.
    /// </summary>
    public static void Queue(SaleUpload sale)
    {
        if (!IsConfigured) return;

        try
        {
            OutboxRepository.Queue(sale.TillReference, JsonSerializer.Serialize(sale, Json));
            Changed?.Invoke(null, EventArgs.Empty);
        }
        catch
        {
            // The sale itself is already saved. Failing to queue it is bad, but taking the
            // till down in front of a customer is worse; the shop can still print and sell.
        }
    }

    // ---------------------------------------------------------------- talking

    /// <summary>Is the server there, and is it one we understand?</summary>
    public static async Task<bool> Ping()
    {
        if (!IsConfigured) return false;

        try
        {
            var hello = await Http.GetFromJsonAsync<Hello>($"{Address}/hello", Json);
            if (hello is null) return Fail("The server answered with nothing.");

            if (hello.Version != Contracts.Version)
                return Fail($"The back office is version {hello.Version} and this till is "
                          + $"{Contracts.Version}. Update them both.");

            ShopName = hello.Shop;
            return Succeed();
        }
        catch (Exception error)
        {
            return Fail(Explain(error));
        }
    }

    /// <summary>
    /// Pulls the catalogue, asking only for what has changed. Returns how many products were
    /// written; -1 when nothing was needed and 0 or more when the copy was replaced.
    /// </summary>
    public static async Task<int> PullCatalogue()
    {
        if (!IsConfigured) return -1;

        try
        {
            var page = await Http.GetFromJsonAsync<CatalogPage>(
                $"{Address}/catalog?since={Uri.EscapeDataString(CatalogSync.Stamp)}", Json);

            if (page is null) { Fail("The server sent no catalogue."); return -1; }

            Succeed();

            // Not complete means nothing had changed, so the copy on this till still stands.
            if (!page.Complete) return -1;

            var written = CatalogSync.Apply(page.Items);
            CatalogSync.Stamp = page.Stamp;
            Catalog.Reload();
            return written;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return -1;
        }
    }

    /// <summary>
    /// Pulls the staff list, so the sign-in box on this till knows who works here and can
    /// check their password with nothing plugged in. Returns how many people came down.
    /// </summary>
    public static async Task<int> PullStaff()
    {
        if (!IsConfigured) return 0;

        try
        {
            var staff = await Http.GetFromJsonAsync<List<StaffMember>>($"{Address}/staff", Json);
            if (staff is null) return 0;

            Succeed();
            return WorkerRepository.ReplaceFromServer(staff);
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return 0;
        }
    }

    /// <summary>
    /// Hands over everything waiting. Returns how many the server accepted.
    ///
    /// A sale the server has seen before counts as accepted: that is the whole point of the
    /// reference, and a till that kept retrying a sale already on the books would never empty
    /// its queue.
    /// </summary>
    public static async Task<int> PushSales()
    {
        if (!IsConfigured) return 0;

        var waiting = OutboxRepository.Pending();
        if (waiting.Count == 0) return 0;

        var sales = new List<SaleUpload>();
        foreach (var row in waiting)
        {
            try
            {
                var sale = JsonSerializer.Deserialize<SaleUpload>(row.Payload, Json);
                if (sale is not null) sales.Add(sale);
                else OutboxRepository.MarkFailed(row.Reference, "The queued sale could not be read back.");
            }
            catch (Exception error)
            {
                OutboxRepository.MarkFailed(row.Reference, error.Message);
            }
        }

        if (sales.Count == 0) return 0;

        try
        {
            var response = await Http.PostAsJsonAsync($"{Address}/sales", new SaleBatch(sales), Json);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SaleBatchResult>(Json);
            if (result is null) { Fail("The server did not say what it did with the sales."); return 0; }

            foreach (var accepted in result.Accepted)
                OutboxRepository.MarkSent(accepted.TillReference, accepted.InvoiceNumber);

            foreach (var rejected in result.Rejected)
                OutboxRepository.MarkFailed(rejected, "The server could not save this sale.");

            Succeed();
            return result.Accepted.Count;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return 0;
        }
    }

    /// <summary>
    /// The whole exchange: is it there, what has changed, what do we owe it. Safe to call on
    /// a timer and safe to call while nothing is configured.
    /// </summary>
    public static async Task Sync()
    {
        if (!IsConfigured) return;
        if (!await Ping()) return;

        await PullCatalogue();
        await PullStaff();
        await PushSales();

        LastSyncedAt = DateTime.Now;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- state

    private static bool Succeed()
    {
        var was = IsOnline;
        IsOnline = true;
        LastProblem = string.Empty;
        if (!was) Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }

    private static bool Fail(string problem)
    {
        var was = IsOnline;
        IsOnline = false;
        LastProblem = problem;
        if (was) Changed?.Invoke(null, EventArgs.Empty);
        return false;
    }

    /// <summary>
    /// Turns a network exception into something worth reading. Nobody standing at a till can
    /// do anything with "No connection could be made because the target machine actively
    /// refused it", but "the back office computer is not answering" tells them where to walk.
    /// </summary>
    private static string Explain(Exception error) => error switch
    {
        TaskCanceledException => "The back office is not answering. It may be asleep.",
        HttpRequestException => $"Cannot reach the back office at {Address}. "
                              + "Check it is switched on and both machines are on the shop's wifi.",
        _ => error.Message,
    };
}
