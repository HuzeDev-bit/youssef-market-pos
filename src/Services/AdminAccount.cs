namespace MarketPos.Services;

/// <summary>
/// The owner's password for admin-only screens.
///
/// Stored as a PBKDF2-SHA256 hash with a per-install random salt (see <see cref="PasswordHash"/>)
/// — never in plain text, and never recoverable, including the starting one.
///
/// <para>
/// A new install starts with <see cref="Starting"/> rather than with nothing. The back office
/// holds the takings, the cost prices and the staff, and leaving it open until somebody thinks
/// to close it is the wrong way round: the shop should have to open it. It is a starting
/// password and not a secret — it is the same on every install and the sign-in screen says so —
/// so the owner is told, once, to change it.
/// </para>
/// </summary>
public static class AdminAccount
{
    /// <summary>What a new install opens with, until the owner changes it.</summary>
    public const string Starting = "123456";

    /// <summary>
    /// Gives a new install its starting password. Called once, at startup, before any screen
    /// asks whether there is one.
    ///
    /// The flag is what makes it once. Without it, an owner who turned the password off in the
    /// back office would be asked for 123456 again after the next restart — the app deciding
    /// it knew better than the person who owns the shop.
    /// </summary>
    public static void StartWithTheDefault()
    {
        if (AppSettings.Current.AdminPasswordStarted) return;

        AppSettings.Current.AdminPasswordStarted = true;
        AppSettings.Current.Save();

        if (!IsConfigured) SetPassword(Starting);
    }

    /// <summary>
    /// True while the back office is still on the password every install ships with. The
    /// sign-in screen says so, because a password everybody knows protects nothing and the
    /// only useful thing to do about it is change it.
    /// </summary>
    public static bool IsStillTheStartingOne => Verify(Starting);

    /// <summary>False on a fresh install — the owner is asked to choose a password first.</summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppSettings.Current.AdminPasswordHash) &&
        !string.IsNullOrWhiteSpace(AppSettings.Current.AdminPasswordSalt);

    public static void SetPassword(string password)
    {
        var (hash, salt) = PasswordHash.Create(password);
        AppSettings.Current.AdminPasswordHash = hash;
        AppSettings.Current.AdminPasswordSalt = salt;
        AppSettings.Current.Save();
    }

    /// <summary>
    /// Turns the password off. The back office then opens on a single Unlock press —
    /// which is the right default for a shop that has not chosen a password, and a
    /// deliberate choice rather than a state it can drift into.
    /// </summary>
    public static void ClearPassword()
    {
        AppSettings.Current.AdminPasswordHash = string.Empty;
        AppSettings.Current.AdminPasswordSalt = string.Empty;
        AppSettings.Current.Save();
    }

    public static bool Verify(string password) =>
        IsConfigured &&
        PasswordHash.Verify(password, AppSettings.Current.AdminPasswordHash,
                            AppSettings.Current.AdminPasswordSalt);
}
