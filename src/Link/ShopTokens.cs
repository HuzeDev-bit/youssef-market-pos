using System.Security.Cryptography;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// Who a request is from, decided by the shop rather than claimed by the client.
///
/// <para>
/// The shop signs a cashier or an owner in against its own database, and hands back a token
/// that means nothing except to this server: a random 256 bits, kept in a table here alongside
/// the person it belongs to, their role, and when it stops being good. Everything a client
/// sends afterwards is that token and nothing else. The client never says what it is allowed to
/// do, because a client that could say so could say anything.
/// </para>
///
/// <para>
/// The server unlocks itself as the owner at start-up so its own back office works. That must
/// not reach a request arriving over the network, and it does not: every request is run under
/// the identity its token names, and a request with no valid token is refused before it reaches
/// a handler at all. A token the server has never issued, or has forgotten, or that has been
/// signed out, is not "probably the owner" — it is nobody.
/// </para>
///
/// <para>
/// Tokens live in memory and die with the server. That is the right lifetime for a shop: a
/// restart is a new day, and a till proves who it is again in one call. Nothing here is ever
/// written to a log — not the token, and certainly not the password that earned it.
/// </para>
/// </summary>
public static class ShopTokens
{
    private sealed record Holder(
        string Name,
        WorkerRole Role,
        int WorkerId,
        DateTime Expires,
        DateTime LastSeen);

    private static readonly Dictionary<string, Holder> Known = new(StringComparer.Ordinal);
    private static readonly object Lock = new();

    /// <summary>
    /// How long a sign-in lasts. A shop's day plus the overrun, and no longer: a token left
    /// good for a week is a token still good on a machine that walked out of the shop.
    /// </summary>
    private static readonly TimeSpan Lasts = TimeSpan.FromHours(14);

    /// <summary>Issues a token for somebody the shop has just checked. Never called otherwise.</summary>
    public static string Issue(string name, WorkerRole role, int workerId = 0)
    {
        // Random, not derived from anything. A token that could be worked out from a name, a
        // clock or a counter is a token somebody else can work out.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        lock (Lock)
        {
            Sweep();
            Known[token] = new Holder(name, role, workerId,
                                      DateTime.UtcNow + Lasts, DateTime.UtcNow);
        }

        return token;
    }

    /// <summary>Ends a session. The token is no good from this moment, on every request.</summary>
    public static void Revoke(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        lock (Lock) Known.Remove(token);
    }

    /// <summary>Whether this token still names somebody. Used to answer 401 before doing work.</summary>
    public static bool IsGood(string? token)
    {
        lock (Lock) return Find(token) is not null;
    }

    /// <summary>Who the token names, for a screen that wants to show it. Empty when nobody.</summary>
    public static (string Name, WorkerRole Role) Whose(string? token)
    {
        lock (Lock)
        {
            var who = Find(token);
            return who is null ? (string.Empty, WorkerRole.Cashier) : (who.Name, who.Role);
        }
    }

    /// <summary>
    /// Runs a piece of work as whoever the token belongs to, and puts the session back after.
    ///
    /// <para>
    /// The identity is the server's own static session, which is what every repository checks
    /// its permissions against — so this is where a remote request stops being anonymous and
    /// starts being a person the repositories can refuse. Serialised, because that session is
    /// one thing shared by every thread the web server answers on, and two requests wearing
    /// each other's identity is the one bug in this file that would matter.
    /// </para>
    ///
    /// <para>
    /// Throws <see cref="NotSignedIn"/> when the token names nobody. The caller turns that into
    /// a 401; what it must never do is carry on as whatever the server happened to be unlocked
    /// as.
    /// </para>
    /// </summary>
    public static T As<T>(string? token, Func<T> work)
    {
        lock (Lock)
        {
            var who = Find(token) ?? throw new NotSignedIn();

            if (who.Role == WorkerRole.Owner) Session.UnlockAsOwner(who.Name);
            else
            {
                Session.SignIn(new Worker
                {
                    Id = who.WorkerId,
                    Name = who.Name,
                    Role = who.Role,
                    IsActive = true,
                });
            }

            try { return work(); }
            finally { Session.SignOut(); }
        }
    }

    private static Holder? Find(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (!Known.TryGetValue(token, out var who)) return null;

        if (DateTime.UtcNow >= who.Expires)
        {
            Known.Remove(token);
            return null;
        }

        Known[token] = who with { LastSeen = DateTime.UtcNow };
        return who;
    }

    private static void Sweep()
    {
        var stale = Known.Where(k => DateTime.UtcNow >= k.Value.Expires)
                         .Select(k => k.Key)
                         .ToList();

        foreach (var token in stale) Known.Remove(token);
    }
}

/// <summary>
/// The request named nobody the shop knows: no token, one it never issued, one it has
/// forgotten, or one that was signed out. Answered with 401, never with the benefit of the
/// doubt.
/// </summary>
public sealed class NotSignedIn : Exception
{
    public NotSignedIn() : base("This request did not say who it was from.") { }
}
