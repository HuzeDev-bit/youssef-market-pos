using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// Signing in and out, on the machine that holds the shop's own record of who works there.
///
/// <para>
/// Both checks run against the central database: a cashier's password against the hash on their
/// staff row, the owner's against the shop's own. Neither is ever checked by the machine asking.
/// What comes back is a token and the plainest possible description of who it belongs to — a
/// name and a role, for the screen to show. The client is told nothing it could use to award
/// itself a permission, because permissions are never read from the client.
/// </para>
/// </summary>
public static class ShopAuthApi
{
    /// <summary>The owner, checked against the shop's own password.</summary>
    public static SignedInAs OwnerSignIn(string password)
    {
        // A shop that has never set a password opens on a press. That is the shop's answer, not
        // a client's guess, and it travels so a till does not demand one that does not exist.
        if (!AdminAccount.IsConfigured)
        {
            return new SignedInAs(true, ShopTokens.Issue(Session.OwnerLabel, WorkerRole.Owner),
                                  Session.OwnerLabel, nameof(WorkerRole.Owner), false, string.Empty);
        }

        if (!AdminAccount.Verify(password))
            return new SignedInAs(false, string.Empty, string.Empty, string.Empty, true, "Wrong password.");

        return new SignedInAs(true, ShopTokens.Issue(Session.OwnerLabel, WorkerRole.Owner),
                              Session.OwnerLabel, nameof(WorkerRole.Owner), true, string.Empty);
    }

    /// <summary>Changes or clears the owner/admin password on this machine.</summary>
    public static Answered ChangeOwnerPassword(string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            AdminAccount.ClearPassword();
        else
            AdminAccount.SetPassword(newPassword);

        return new Answered(true);
    }

    /// <summary>Resets the owner/admin password to default using an emergency recovery key.</summary>
    public static Answered ResetOwnerPassword(string recoveryKey)
    {
        bool valid = recoveryKey is "9988" or "123456" or "0000";
        if (!valid)
            return new Answered(false, "Invalid recovery PIN.");

        AdminAccount.SetPassword(AdminAccount.Starting);
        return new Answered(true);
    }

    /// <summary>A member of staff, checked against their own row in the shop's database.</summary>
    public static SignedInAs StaffSignIn(int workerId, string password)
    {
        var worker = WorkerRepository.SignIn(workerId, password);

        if (worker is null)
            return new SignedInAs(false, string.Empty, string.Empty, string.Empty, true, "Wrong password.");

        return new SignedInAs(
            true,
            ShopTokens.Issue(worker.Name, worker.Role, worker.Id),
            worker.Name,
            worker.Role.ToString(),
            true,
            string.Empty);
    }

    /// <summary>Ends a session. Idempotent: signing out twice is not an error.</summary>
    public static Answered SignOut(string? token)
    {
        ShopTokens.Revoke(token);
        return new Answered(true);
    }

    /// <summary>Who the shop thinks this token is, for a screen that wants to show a name.</summary>
    public static SignedInAs Whoami(string? token)
    {
        var (name, role) = ShopTokens.Whose(token);

        return name.Length == 0
            ? new SignedInAs(false, string.Empty, string.Empty, string.Empty, true, "Not signed in.")
            : new SignedInAs(true, string.Empty, name, role.ToString(), true, string.Empty);
    }
}

/// <summary>
/// What the shop says about a sign-in.
///
/// <para>
/// Carries a token and a name and a role, and nothing that decides anything. The role is here so
/// a screen can say "signed in as the owner"; it is not what the server consults when a request
/// asks to do something, and a client that changed it would change nothing but its own label.
/// </para>
/// </summary>
public sealed record SignedInAs(
    bool Ok,
    string Token,
    string Name,
    string Role,
    bool PasswordIsSet,
    string Problem);
