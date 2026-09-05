namespace MarketPos.Services;

/// <summary>
/// The owner's password for admin-only screens.
///
/// Stored as a PBKDF2-SHA256 hash with a per-install random salt (see <see cref="PasswordHash"/>)
/// — never in plain text, and never recoverable. There is deliberately no default: a shipped
/// password nobody changes is the same as no password.
/// </summary>
public static class AdminAccount
{
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
