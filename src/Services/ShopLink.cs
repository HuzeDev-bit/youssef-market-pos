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
    // Short on purpose. This runs while somebody is standing at a counter; a request that
    // hangs for thirty seconds has already failed as far as the shop is concerned. Pinned to
    // the paired shop, so the token it carries goes nowhere else.
    private static readonly HttpClient Http = Link.PinnedShop.Client(TimeSpan.FromSeconds(6));

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

    /// <summary>
    /// Sales taken here that the server has not confirmed. Never any, on a till: a till makes
    /// no sale of its own to hold on to, and has no database to hold one in.
    /// </summary>
    public static int Waiting
    {
        get
        {
            if (Catalog.BelongsToAServer) return 0;
            try { return OutboxRepository.PendingCount(); } catch { return 0; }
        }
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

            // Not complete means nothing had changed, so what is already in memory still stands.
            if (!page.Complete) return -1;

            // A till puts the shop's answer straight into memory. It is not written to this
            // computer at all: there is no products table to go stale, nothing for a second
            // copy of the app to find, and nothing that can be shown when the shop is not
            // answering. What is on screen came over the wire or it is not on screen.
            if (Catalog.BelongsToAServer)
            {
                Catalog.TakeFromTheServer(page.Items.Select(item => new Models.Product
                {
                    Id = item.Id,
                    Barcode = item.Barcode,
                    Name = item.Name,
                    Category = item.Category,
                    Price = item.Price,
                    TaxRate = item.TaxRate,
                    Unit = item.Unit == nameof(Models.Unit.Kg) ? Models.Unit.Kg : Models.Unit.Each,
                    Stock = item.Stock,

                    // Not a path -- this machine has no picture files. A token the tile's
                    // converter knows how to turn back into a photo by asking the shop.
                    ImagePath = item.HasPhoto ? ShopImages.ProductToken(item.Id) : null,
                }));

                CatalogSync.Stamp = page.Stamp;
                return page.Items.Count;
            }

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

            // A till is handed the staff list every time it needs one and writes none of it
            // down. Storing it locally put a copy of every cashier's password hash on the
            // cashier's own machine, to answer a question the shop is there to answer.
            if (Catalog.BelongsToAServer) return staff.Count;

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

        // Never from a till. The outbox is the old arrangement: ring the sale up here, keep it
        // in a local queue, hand it over later. A till makes no sale of its own now — it asks
        // the shop to make one and waits — so there is nothing to queue, and the queue itself
        // lives in a database this machine does not have.
        if (Catalog.BelongsToAServer) return 0;

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
    /// Puts a product a cashier filled in at the counter into the shop's own database.
    ///
    /// <para>
    /// Straight to the server, and not into this machine's copy. Everything in a till's
    /// database arrived from the server and is overwritten by the server on the next sync, so
    /// a product written only here would be sold once and then quietly disappear — and would
    /// never reach the back office, the stock list, or the till on the other counter.
    /// </para>
    ///
    /// <para>
    /// This one is not queued like a sale is. A sale has already happened and the money is in
    /// the drawer whatever the network is doing; a product that has not reached the books is
    /// not yet a product, and the cashier standing there is the only person who can be told so.
    /// </para>
    /// </summary>
    public static async Task<ProductAccepted?> AddProduct(NewProduct product)
    {
        if (!IsConfigured) return null;

        try
        {
            var response = await Http.PostAsJsonAsync($"{Address}/products/scanned", product, Json);
            response.EnsureSuccessStatusCode();

            var made = await response.Content.ReadFromJsonAsync<ProductAccepted>(Json);
            if (made is null) { Fail("The server did not say what it did with the product."); return null; }

            Succeed();
            return made;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>
    /// Asks the shop's server to make a sale, and waits for it.
    ///
    /// <para>
    /// This is the till's checkout. Not a copy of a sale already made here — the till makes no
    /// sale. The server writes the ticket, its lines, the stock and the movements in one
    /// transaction over the shop's own database, and answers with the invoice number or the
    /// reason there is not one. Until that answer arrives nothing has been sold.
    /// </para>
    ///
    /// <para>
    /// The reference travels with it so a retry after a timeout cannot bank the same sale
    /// twice: the server recognises it and hands back the invoice number it gave the first
    /// time. That is why a lost answer is survivable and why the till may safely ask again.
    /// </para>
    /// </summary>
    public static async Task<CheckoutDone> Checkout(SaleUpload sale)
    {
        if (!IsConfigured)
            return new CheckoutDone(false, 0, false, "This till has no shop to sell for.");

        try
        {
            var response = await Http.PostAsJsonAsync($"{Address}/checkout", sale, Json);

            // A refusal is an answer, not a failure: the server says why, in words the cashier
            // can act on — most often that the last one went on the other counter.
            var said = await response.Content.ReadFromJsonAsync<CheckoutDone>(Json);
            if (said is not null)
            {
                if (said.Ok) Succeed();
                return said;
            }

            Fail("The server did not say what it did with the sale.");
            return new CheckoutDone(false, 0, false, LastProblem);
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return new CheckoutDone(false, 0, false, LastProblem);
        }
    }

    // ---------------------------------------------------------------- what a till asks for
    //
    // Each of these is a question a till used to answer out of a database on the cashier's own
    // machine. It has no such database — its catalogue is a copy the shop sends and is thrown
    // away when the app closes — so every one of them is asked of the shop instead. They all
    // fail the same way: null, with the reason on the link's status, and never a stale answer
    // dressed up as a current one.

    /// <summary>What a scanned or typed thing is, and what it costs.</summary>
    public static async Task<PriceAnswer?> PriceCheck(string query, bool forTheOwner)
    {
        if (!IsConfigured) return null;

        try
        {
            var answer = await Http.GetFromJsonAsync<PriceAnswer>(
                $"{Address}/pricecheck?q={Uri.EscapeDataString(query)}&owner={forTheOwner}", Json);

            if (answer is not null) Succeed();
            return answer;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>The shop's tickets, today's takings, and the numbers a reprint may offer.</summary>
    public static async Task<TicketList?> Tickets(string? search)
    {
        if (!IsConfigured) return null;

        try
        {
            var where = $"{Address}/tickets";
            if (!string.IsNullOrWhiteSpace(search))
                where += $"?search={Uri.EscapeDataString(search)}";

            var list = await Http.GetFromJsonAsync<TicketList>(where, Json);
            if (list is not null) Succeed();
            return list;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>One whole ticket, for printing or reprinting. Null when the shop has no such sale.</summary>
    public static async Task<TicketDetail?> Ticket(int invoiceNumber)
    {
        if (!IsConfigured) return null;

        try
        {
            var response = await Http.GetAsync($"{Address}/tickets/{invoiceNumber}");

            // A ticket that is not there is an answer, and the shop gave it: the link is fine.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Succeed();
                return null;
            }

            response.EnsureSuccessStatusCode();

            var ticket = await response.Content.ReadFromJsonAsync<TicketDetail>(Json);
            if (ticket is not null) Succeed();
            return ticket;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>The settings the whole shop shares.</summary>
    public static async Task<ShopWideSettings?> Settings()
    {
        if (!IsConfigured) return null;

        try
        {
            var settings = await Http.GetFromJsonAsync<ShopWideSettings>($"{Address}/settings", Json);
            if (settings is not null) Succeed();
            return settings;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>
    /// Asks the shop whether this is the owner's password.
    ///
    /// Null means the shop could not be asked, which is not the same as a wrong password and
    /// must never be treated as one — nor as a right one.
    /// </summary>
    public static async Task<SignedInAs?> SignInAsOwner(string password)
    {
        if (!IsConfigured) return null;

        try
        {
            var response = await Http.PostAsJsonAsync($"{Address}/auth/owner/signin",
                new OwnerSignIn(password), Json);

            // A wrong password is the shop answering, not the link failing.
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized)
            {
                Succeed();
                return await response.Content.ReadFromJsonAsync<SignedInAs>(Json);
            }

            response.EnsureSuccessStatusCode();

            var said = await response.Content.ReadFromJsonAsync<SignedInAs>(Json);
            if (said is null) return null;

            Succeed();

            // Held for as long as the app is open, and sent with everything after this. It is
            // what the shop checks; nothing this machine says about itself counts.
            if (said.Ok) ShopSession.Keep(said.Token, said.Name);

            return said;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>The shop's categories.</summary>
    public static async Task<List<CategoryName>?> Categories() => await Ask<List<CategoryName>>("categories");

    /// <summary>The shop's suppliers.</summary>
    public static async Task<List<SupplierName>?> Suppliers() => await Ask<List<SupplierName>>("suppliers/names");

    /// <summary>One plain GET, for the answers that are just a list.</summary>
    private static async Task<T?> Ask<T>(string what) where T : class
    {
        if (!IsConfigured) return null;

        try
        {
            var answer = await Http.GetFromJsonAsync<T>($"{Address}/{what}", Json);
            if (answer is not null) Succeed();
            return answer;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>Who may sign in at a till, as the shop knows them.</summary>
    public static async Task<List<StaffMember>?> Staff()
    {
        if (!IsConfigured) return null;

        try
        {
            var staff = await Http.GetFromJsonAsync<List<StaffMember>>($"{Address}/staff", Json);
            if (staff is not null) Succeed();
            return staff;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>
    /// Checks a cashier's password, on the shop's machine.
    ///
    /// The password never leaves this method, and the answer never comes from this machine:
    /// a till that decided for itself who was allowed to sign in would be a till anybody could
    /// let themselves into by unplugging the network.
    /// </summary>
    public static async Task<SignedIn?> SignIn(int workerId, string password)
    {
        if (!IsConfigured) return null;

        try
        {
            var response = await Http.PostAsJsonAsync($"{Address}/auth/staff/signin",
                new SignInRequest(workerId, password), Json);

            // A wrong password is an answer, not a failure of the link.
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Succeed();
                return null;
            }

            response.EnsureSuccessStatusCode();

            var said = await response.Content.ReadFromJsonAsync<SignedInAs>(Json);
            if (said is null || !said.Ok) return null;

            Succeed();
            ShopSession.Keep(said.Token, said.Name);

            return new SignedIn(workerId, said.Name, said.Role);
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
        }
    }

    /// <summary>Waits for one of the calls above on a thread that is not the one drawing the till.</summary>
    public static T Now<T>(Func<Task<T>> ask) =>
        Task.Run(ask).GetAwaiter().GetResult();

    /// <summary>Whether the shop's server is answering, and what it says it is serving.</summary>
    public static async Task<Health?> Ask()
    {
        if (!IsConfigured) return null;

        try
        {
            var health = await Http.GetFromJsonAsync<Health>($"{Address}/health", Json);
            if (health is not null) Succeed();
            return health;
        }
        catch (Exception error)
        {
            Fail(Explain(error));
            return null;
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
