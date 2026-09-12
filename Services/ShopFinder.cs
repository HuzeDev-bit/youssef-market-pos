using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MarketPos.Link;

namespace MarketPos.Services;

/// <summary>
/// Finds the machine that holds the shop, so that nobody has to know what an IP address is.
///
/// <para>
/// Setting a second till up used to mean somebody reading a number off the server's screen and
/// typing it into the till without a digit wrong. The shop owner does not have a screen on that
/// machine, does not know what ipconfig is, and should not have to.
/// </para>
///
/// <para>
/// So it asks for the shop by name first. The server machine is called <c>pos-server</c>, and a
/// name is the one thing about it that does not change when the router hands out addresses
/// again after a power cut — which is the failure that has every till in the shop pointing at
/// nothing on a Monday morning. Only if nothing answers to the name does it fall back to
/// knocking on every address on the shop's own network.
/// </para>
///
/// <para>
/// Everything here speaks HTTPS, because the shop's server does. It used to knock in plain HTTP
/// while the server had already moved to TLS, so the search could never find anything and every
/// till in the shop sat there saying it was working offline.
/// </para>
/// </summary>
public static class ShopFinder
{
    /// <summary>The shop's server, and the name it gave. Null when nothing answered.</summary>
    public sealed record Found(string Address, string ShopName);

    /// <summary>The port the shop's server listens on. Nobody types it.</summary>
    public const int Port = 5000;

    /// <summary>
    /// What the server machine is called. The setup script names it this, and the shop's
    /// certificate carries the name, so a till can ask for it without knowing any numbers.
    /// </summary>
    public const string Name = "pos-server";

    /// <summary>The address a till tries before it tries anything else.</summary>
    public static string Expected => $"http://{Name}:{Port}";

    /// <summary>
    /// Asks for the shop by name, and knocks on the neighbours only if the name goes unanswered.
    ///
    /// A quarter of a second to open a socket and a second to answer: a machine that is there
    /// answers immediately, and one that is not never will. Every address is tried at the same
    /// time, so the whole sweep takes about as long as the slowest single one.
    /// </summary>
    public static async Task<Found?> Look(CancellationToken stop = default)
    {
        // Never on the caller's thread. Two hundred and fifty sockets can be opened below, and
        // the one thread this must not sit on is the one drawing the window that asked.
        await Task.Yield();

        using var byName = CancellationTokenSource.CreateLinkedTokenSource(stop);
        using var naming = Client();

        if (await Knock(naming, Name, byName).ConfigureAwait(false) is { } known) return known;

        if (Link.PinnedShop.LastRefusal.Length > 0)
        {
            Link.PinnedShop.Forget();

            using var again = Client();
            if (await Knock(again, Name, byName).ConfigureAwait(false) is { } after) return after;
        }

        var candidates = Neighbours().ToList();
        if (candidates.Count == 0) return null;

        using var http = Client();
        using var firstAnswer = CancellationTokenSource.CreateLinkedTokenSource(stop);

        var found = await Sweep(http, candidates, firstAnswer).ConfigureAwait(false);
        if (found is not null) return found;

        if (Link.PinnedShop.LastRefusal.Length > 0)
        {
            Link.PinnedShop.Forget();

            using var clear = Client();
            using var second = CancellationTokenSource.CreateLinkedTokenSource(stop);
            return await Sweep(clear, candidates, second).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<Found?> Sweep(HttpClient http, List<IPAddress> candidates,
                                            CancellationTokenSource firstAnswer)
    {
        var knocks = candidates.Select(a => Knock(http, a.ToString(), firstAnswer)).ToList();

        while (knocks.Count > 0)
        {
            var done = await Task.WhenAny(knocks).ConfigureAwait(false);
            knocks.Remove(done);

            if (await done.ConfigureAwait(false) is not { } shop) continue;

            firstAnswer.Cancel();
            return shop;
        }

        return null;
    }

    private static HttpClient Client() => PinnedShop.Client(TimeSpan.FromSeconds(2));

    private static async Task<Found?> Knock(HttpClient http, string host, CancellationTokenSource stop)
    {
        try
        {
            using (var knock = new TcpClient())
            {
                var open = knock.ConnectAsync(host, Port, stop.Token).AsTask();
                if (await Task.WhenAny(open, Task.Delay(400, stop.Token)).ConfigureAwait(false) != open)
                    return null;

                await open.ConfigureAwait(false);
            }

            string? said = null;
            string scheme = "http";
            try
            {
                said = await http.GetStringAsync($"http://{host}:{Port}/hello", stop.Token).ConfigureAwait(false);
            }
            catch
            {
                scheme = "https";
                said = await http.GetStringAsync($"https://{host}:{Port}/hello", stop.Token).ConfigureAwait(false);
            }

            var shop = System.Text.Json.JsonDocument.Parse(said).RootElement;
            if (!shop.TryGetProperty("shop", out var name)) return null;

            return new Found($"{scheme}://{host}:{Port}", name.GetString() ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Whether an address found is this very machine.</summary>
    public static bool IsThisMachine(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var found)) return false;

        // By name as well as by number. A server reached as "pos-server" from the machine that
        // is itself pos-server has no IP address in the URL to compare, and answering "not me"
        // would let a till mirror its own database while the chip says, truthfully and
        // uselessly, that it is connected.
        if (string.Equals(found.Host, Dns.GetHostName(), StringComparison.OrdinalIgnoreCase))
            return true;

        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(card => card.GetIPProperties().UnicastAddresses)
            .Any(here => here.Address.ToString() == found.Host);
    }

    /// <summary>
    /// Every address on the same little network as this machine.
    ///
    /// Only real, up, non-loopback networks, and only the ones small enough to be a shop's —
    /// a /24 is 254 machines and a corporate /16 is sixty-five thousand, which is not a
    /// network this is meant to search and not a network a grocery has.
    /// </summary>
    private static IEnumerable<IPAddress> Neighbours()
    {
        foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (card.OperationalStatus != OperationalStatus.Up) continue;
            if (card.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var here in card.GetIPProperties().UnicastAddresses)
            {
                if (here.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (here.PrefixLength < 24) continue;

                var mine = here.Address.GetAddressBytes();

                // This machine included. A shop that runs the server on the same computer as
                // the till gets a true answer rather than silence, and the screen that asked
                // is the right place to say what that answer means.
                for (var last = 1; last < 255; last++)
                    yield return new IPAddress(new[] { mine[0], mine[1], mine[2], (byte)last });
            }
        }
    }
}
