using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MarketPos.Services;

/// <summary>
/// Finds the machine that holds the shop, so that nobody has to know what an IP address is.
///
/// <para>
/// Setting a second till up used to mean somebody reading a number off the server's screen and
/// typing it into the till without a digit wrong. The shop owner does not have a screen on that
/// machine, does not know what ipconfig is, and should not have to: the two machines are on the
/// same little network with a handful of addresses on it, and the shop's server is the one that
/// answers when asked who it is.
/// </para>
///
/// <para>
/// So this knocks on every address on the shop's own network at once and keeps the one that
/// answers as the shop. It asks nothing of the server that the tills do not already ask of it
/// every minute, which means a shop running an older server is found just the same.
/// </para>
/// </summary>
public static class ShopFinder
{
    /// <summary>The shop's server, and the name it gave. Null when nothing answered.</summary>
    public sealed record Found(string Address, string ShopName);

    private const int Port = 5000;

    /// <summary>
    /// Knocks on this machine's own network and answers with the first shop that replies.
    ///
    /// A quarter of a second to open a socket and a second to answer: a machine that is there
    /// answers immediately, and one that is not never will. Every address is tried at the same
    /// time, so the whole thing takes about as long as the slowest single one.
    /// </summary>
    public static async Task<Found?> Look(CancellationToken stop = default)
    {
        // Never on the caller's thread. Two hundred and fifty sockets are opened here, and the
        // one thread this must not sit on is the one drawing the window that asked.
        await Task.Yield();

        var candidates = Neighbours().ToList();
        if (candidates.Count == 0) return null;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var firstAnswer = CancellationTokenSource.CreateLinkedTokenSource(stop);

        var knocks = candidates.Select(address => Knock(http, address, firstAnswer)).ToList();

        while (knocks.Count > 0)
        {
            var done = await Task.WhenAny(knocks).ConfigureAwait(false);
            knocks.Remove(done);

            if (await done.ConfigureAwait(false) is not { } shop) continue;

            // Everything else can stop knocking.
            firstAnswer.Cancel();
            return shop;
        }

        return null;
    }

    private static async Task<Found?> Knock(HttpClient http, IPAddress address, CancellationTokenSource stop)
    {
        try
        {
            // The port first, on its own. Opening a socket to a machine that is not there fails
            // in milliseconds, where an HTTP call to the same address waits out its whole
            // timeout — the difference between a search that takes a second and one that takes
            // four minutes.
            using (var knock = new TcpClient())
            {
                var open = knock.ConnectAsync(address, Port, stop.Token).AsTask();
                if (await Task.WhenAny(open, Task.Delay(400, stop.Token)).ConfigureAwait(false) != open)
                    return null;

                await open.ConfigureAwait(false);   // rethrows a refusal: a machine that is there and is not the shop
            }

            var said = await http.GetStringAsync($"http://{address}:{Port}/hello", stop.Token)
                                 .ConfigureAwait(false);

            // Something is listening on 5000; whether it is the shop is another question. The
            // answer has to be ours, and has to name a shop.
            var shop = System.Text.Json.JsonDocument.Parse(said).RootElement;
            if (!shop.TryGetProperty("shop", out var name)) return null;

            return new Found($"http://{address}:{Port}", name.GetString() ?? string.Empty);
        }
        catch
        {
            // Nothing there, something else there, or the search is over. All the same answer.
            return null;
        }
    }

    /// <summary>Whether an address found is this very machine.</summary>
    public static bool IsThisMachine(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var found)
        && NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(card => card.GetIPProperties().UnicastAddresses)
            .Any(here => here.Address.ToString() == found.Host);

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
