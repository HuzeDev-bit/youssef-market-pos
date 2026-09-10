using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// One way in and out for every business request a screen makes of the shop.
///
/// <para>
/// The back office was written against repositories that answer immediately and never fail for
/// want of a network. Those pages are unchanged; what they call now is a service that goes over
/// the wire, and this is where that happens — once, so the address, the timeout, the JSON and
/// the failure behaviour are the same for every screen.
/// </para>
///
/// <para>
/// A shop that cannot be reached throws <see cref="ShopUnreachable"/>. It does not return an
/// empty list. An empty list is a business answer — this shop has no suppliers — and a page
/// that cannot tell that apart from a dead network will eventually show an owner a blank
/// screen and let them believe it.
/// </para>
/// </summary>
public static class Api
{
    // Talks to the paired shop and refuses anything else. Not a client that accepts any
    // certificate: that would encrypt the wire and then hand it to whoever asked.
    private static readonly HttpClient Http = PinnedShop.Client(TimeSpan.FromSeconds(20));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string Address => AppSettings.Current.ServerAddress.Trim().TrimEnd('/');

    /// <summary>The header a request proves itself with. Named once so both ends agree.</summary>
    public const string TokenHeader = "X-Shop-Token";

    public static T? Get<T>(string what) where T : class => Send<T>(HttpMethod.Get, what, null);

    public static T? Post<T>(string what, object body) where T : class =>
        Send<T>(HttpMethod.Post, what, body);

    public static T? Put<T>(string what, object body) where T : class =>
        Send<T>(HttpMethod.Put, what, body);

    public static T? Delete<T>(string what) where T : class =>
        Send<T>(HttpMethod.Delete, what, null);

    private static T? Send<T>(HttpMethod how, string what, object? body) where T : class
    {
        if (Address.Length == 0)
            throw new ShopUnreachable("This machine has not been told where the shop is.");

        try
        {
            // Waited for on a worker thread. The back office was written against a database
            // that answers at once, and a page that awaited would be a page rewritten.
            return Task.Run(async () =>
            {
                using var request = new HttpRequestMessage(how, $"{Address}/{what}");
                if (body is not null) request.Content = JsonContent.Create(body, options: Json);

                // Who this is from. The only thing the client says about itself, and it says
                // nothing about what it may do — the shop decides that from the token.
                if (ShopSession.SignedIn)
                    request.Headers.Add(TokenHeader, ShopSession.Token);

                using var response = await Http.SendAsync(request);

                // A refusal the shop meant — no such thing, not allowed — is an answer, and it
                // is carried in the body for the caller to read.
                // Not signed in, or signed in as somebody who may not do this. Neither is a
                // network fault and neither may be shrugged off: they are thrown so a screen
                // cannot mistake them for an empty result.
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized)
                    throw new NotSignedInAtTheShop();

                if (response.StatusCode is System.Net.HttpStatusCode.Forbidden)
                    throw new UnauthorizedAccessException(
                        "The shop did not allow that. Sign in as somebody who may do it.");

                if (response.StatusCode is System.Net.HttpStatusCode.NotFound
                                        or System.Net.HttpStatusCode.Conflict
                                        or System.Net.HttpStatusCode.BadRequest)
                {
                    return await Read<T>(response);
                }

                response.EnsureSuccessStatusCode();
                return await Read<T>(response);
            }).GetAwaiter().GetResult();
        }
        catch (ShopUnreachable)
        {
            throw;
        }
        catch (NotSignedInAtTheShop)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception problem)
        {
            var reason = problem is AggregateException bundle && bundle.InnerException is not null
                ? bundle.InnerException.Message
                : problem.Message;

            // A refused certificate is not a network fault and must not read like one: the
            // shop is reachable, it is simply not the shop this till was paired with.
            if (PinnedShop.LastRefusal.Length > 0) reason = PinnedShop.LastRefusal;

            throw new ShopUnreachable(reason);
        }
    }

    private static async Task<T?> Read<T>(HttpResponseMessage response) where T : class
    {
        if (response.Content.Headers.ContentLength is 0) return null;

        try { return await response.Content.ReadFromJsonAsync<T>(Json); }
        catch { return null; }
    }
}

/// <summary>
/// The shop could not be asked.
///
/// Thrown rather than swallowed, and never turned into an empty result: "no answer" and "the
/// answer is none" are different things, and a back office that confuses them will tell an
/// owner their stock is gone.
/// </summary>
public sealed class ShopUnreachable : Exception
{
    public ShopUnreachable(string reason)
        : base($"Cannot reach the shop's server. {reason}") { }
}

/// <summary>
/// The shop does not know who this is: no token, or one that has expired or been signed out.
///
/// Its own type so a screen can send the person back to the sign-in box rather than showing
/// them an empty list and letting them believe the shop is empty.
/// </summary>
public sealed class NotSignedInAtTheShop : Exception
{
    public NotSignedInAtTheShop()
        : base("The shop does not know who this till is. Sign in again.") { }
}
