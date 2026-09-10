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
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string Address => AppSettings.Current.ServerAddress.Trim().TrimEnd('/');

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

                using var response = await Http.SendAsync(request);

                // A refusal the shop meant — no such thing, not allowed — is an answer, and it
                // is carried in the body for the caller to read.
                if (response.StatusCode is System.Net.HttpStatusCode.NotFound
                                        or System.Net.HttpStatusCode.Conflict
                                        or System.Net.HttpStatusCode.BadRequest
                                        or System.Net.HttpStatusCode.Forbidden)
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
        catch (Exception problem)
        {
            var reason = problem is AggregateException bundle && bundle.InnerException is not null
                ? bundle.InnerException.Message
                : problem.Message;

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
