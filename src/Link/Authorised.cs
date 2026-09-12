using Microsoft.AspNetCore.Http;

namespace MarketPos.Link;

/// <summary>
/// Runs a business request as whoever its token names, or refuses it.
///
/// <para>
/// Every endpoint that touches the shop's books goes through here. The token is read from the
/// request, the work runs under that person's identity, and the repositories do their own
/// permission checks against it exactly as they do for the owner sitting at the shop's own
/// machine. A request with no token, or with one the shop has forgotten, is 401 — not "probably
/// the owner", which is what it would be if the server simply did the work as itself.
/// </para>
///
/// <para>
/// A refusal on grounds of permission is 403 with the shop's own words. Both are distinct from
/// a shop that cannot be reached, and all three are distinct from an empty result, because a
/// screen that cannot tell those apart eventually tells an owner something untrue about their
/// business.
/// </para>
/// </summary>
public static class Authorised
{
    /// <summary>The work, done as the person the request proves itself to be.</summary>
    public static IResult Do<T>(HttpRequest request, Func<T> work)
    {
        try
        {
            return Results.Ok(ShopTokens.As(Token(request), work));
        }
        catch (NotSignedIn)
        {
            return Results.Unauthorized();
        }
        catch (UnauthorizedAccessException refused)
        {
            // The repository's own sentence, which names the person and what they tried to do.
            return Results.Json(new { problem = refused.Message },
                                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    /// <summary>
    /// The same, for work whose refusals are part of its answer rather than exceptions — a
    /// category that still has products in it, a barcode another product carries.
    /// </summary>
    public static IResult Answering<T>(HttpRequest request, Func<T> work, Func<T, bool> ok,
                                       int whenRefused = StatusCodes.Status409Conflict)
    {
        try
        {
            var said = ShopTokens.As(Token(request), work);
            return ok(said) ? Results.Ok(said) : Results.Json(said, statusCode: whenRefused);
        }
        catch (NotSignedIn)
        {
            return Results.Unauthorized();
        }
        catch (UnauthorizedAccessException refused)
        {
            return Results.Json(new { problem = refused.Message },
                                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static string? Token(HttpRequest request) =>
        request.Headers.TryGetValue(Api.TokenHeader, out var carried)
            ? carried.ToString()
            : null;
}
