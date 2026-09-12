using System.Collections.Concurrent;

namespace MarketPos.Services;

/// <summary>
/// Pictures, on a machine that does not have the picture files.
///
/// <para>
/// Every photo in this app is stored as a file in the shop's own folder, and everything that
/// draws one binds to a path on disk. On the machine that owns the shop that is exactly right.
/// On a cashier's machine there is no such folder and no such file, so every product tile and
/// every category card fell back to a grey placeholder -- the shop had photographed its shelves
/// and the tills showed none of it.
/// </para>
///
/// <para>
/// So a till is given a token instead of a path: <c>shop://product/12</c>. The converter that
/// turns a path into a picture recognises it, asks the shop for the bytes once, and keeps them
/// in memory for as long as the app is open. Nothing is written to the cashier's machine --
/// photos are the shop's, like everything else here, and a till that is switched off should
/// leave nothing of the business behind on it.
/// </para>
/// </summary>
public static class ShopImages
{
    private const string Scheme = "shop://";

    /// <summary>Bytes already fetched, by token. Empty array means "the shop has none".</summary>
    private static readonly ConcurrentDictionary<string, byte[]> Kept = new();

    /// <summary>Whether this is one of ours rather than a path on disk.</summary>
    public static bool IsToken(string? value) =>
        value is not null && value.StartsWith(Scheme, StringComparison.Ordinal);

    /// <summary>What a till should be given for a product that the shop holds a photo for.</summary>
    public static string ProductToken(int id) => $"{Scheme}product/{id}";

    /// <summary>The same for a category card.</summary>
    public static string CategoryToken(int id) => $"{Scheme}category/{id}";

    /// <summary>
    /// Where a product's picture comes from: a file on the shop's own machine, a token on a
    /// till, and nothing at all when there is no picture to show.
    /// </summary>
    public static string? ForProduct(int id, string? localPath, bool shopHasOne) =>
        Catalog.BelongsToAServer
            ? shopHasOne ? ProductToken(id) : null
            : localPath;

    /// <summary>The same for a category, which stores a file name rather than a path.</summary>
    public static string? ForCategory(int id, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        return Catalog.BelongsToAServer ? CategoryToken(id) : CategoryImages.Find(fileName);
    }

    /// <summary>
    /// The picture behind a token, asked of the shop the first time and remembered after.
    ///
    /// Null when the shop has none, cannot be reached, or refused. A missing picture is a tile
    /// without a photo, which is what the screen already draws when a product has none -- it
    /// is never a reason to interrupt somebody at a counter.
    /// </summary>
    public static byte[]? Bytes(string token)
    {
        if (!IsToken(token)) return null;

        if (Kept.TryGetValue(token, out var already))
            return already.Length == 0 ? null : already;

        var got = Ask(token) ?? Array.Empty<byte>();
        Kept[token] = got;

        return got.Length == 0 ? null : got;
    }

    private static byte[]? Ask(string token)
    {
        var what = token[Scheme.Length..];          // "product/12"
        var slash = what.IndexOf('/');
        if (slash <= 0) return null;

        var kind = what[..slash];
        if (!int.TryParse(what[(slash + 1)..], out var id)) return null;

        var where = kind switch
        {
            "product" => $"products/{id}/photo",
            "category" => $"categories/{id}/photo",
            _ => null,
        };

        if (where is null) return null;

        try
        {
            return Link.Api.Bytes(where);
        }
        catch
        {
            // The shop is not answering, or it has nothing. Either way the tile draws without
            // a photo, and the connection chip is already saying which of the two it is.
            return null;
        }
    }

    /// <summary>
    /// Drops what has been remembered, so a photo changed in the back office is fetched again
    /// rather than staying whatever this till saw first.
    /// </summary>
    public static void Forget() => Kept.Clear();
}
