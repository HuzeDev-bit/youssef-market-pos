namespace MarketPos.Link;

/// <summary>
/// The token this machine was given when it signed in, held for as long as the app is open.
///
/// <para>
/// In memory only. It is not written to the settings file, not to a log, and not anywhere else
/// on the cashier's machine: a token on disk is a token that outlives the person who earned it
/// and travels with a stolen laptop. Closing the app forgets it, which is the correct amount of
/// memory for a counter that anyone can walk up to.
/// </para>
/// </summary>
public static class ShopSession
{
    /// <summary>What the shop gave this machine to prove who is at it. Empty when nobody is.</summary>
    public static string Token { get; private set; } = string.Empty;

    /// <summary>The name the shop knows them by, for a screen that wants to show it.</summary>
    public static string Name { get; private set; } = string.Empty;

    public static bool SignedIn => Token.Length > 0;

    public static void Keep(string token, string name)
    {
        Token = token;
        Name = name;
    }

    /// <summary>
    /// Forgets the token here and asks the shop to forget it too.
    ///
    /// Both halves matter. Dropping it locally stops this machine using it; telling the shop
    /// stops anything else that has somehow got hold of it.
    /// </summary>
    public static void SignOut()
    {
        var was = Token;
        Token = string.Empty;
        Name = string.Empty;

        if (was.Length == 0) return;

        try { Api.Post<Answered>("auth/signout", new { }); }
        catch { /* a shop that cannot be reached will forget it when it restarts */ }
    }
}
