using System.Security.Cryptography;

namespace MarketPos.Services;

/// <summary>
/// PBKDF2-SHA256 hashing, shared by the owner's admin password and by cashier till PINs.
///
/// A till sits on a shop counter where staff and customers can reach the machine, so nothing
/// that unlocks money is ever stored in a form that can be read back — only compared against.
/// </summary>
public static class PasswordHash
{
    private const int Iterations = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Hashes a secret with a fresh random salt. Both come back Base64-encoded.</summary>
    public static (string Hash, string Salt) Create(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return (Convert.ToBase64String(Derive(secret, salt)), Convert.ToBase64String(salt));
    }

    public static bool Verify(string secret, string hash, string salt)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt)) return false;

        try
        {
            // Fixed-time compare: a plain == would leak how much of the hash matched.
            return CryptographicOperations.FixedTimeEquals(
                Derive(secret, Convert.FromBase64String(salt)),
                Convert.FromBase64String(hash));
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Derive(string secret, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
}
