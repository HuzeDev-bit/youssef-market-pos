using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MarketPos.Services;

namespace MarketPos.Link;

/// <summary>
/// The one server this machine will talk to.
///
/// <para>
/// The shop's certificate is its own, so nothing in Windows vouches for it and ordinary
/// validation would refuse every connection. The wrong answer to that is to accept any
/// certificate at all, which is what most shops end up doing and which throws away the
/// encryption it was meant to protect: anything on the network can then sit in the middle,
/// take the owner's password on the way past, and hand back whatever it likes.
/// </para>
///
/// <para>
/// What this does instead is pair once and remember. The first time a till reaches a shop it
/// writes down that server's fingerprint. From then on it checks every connection against it and
/// refuses anything else — a different machine, a different key, anything in between. A
/// certificate that changes is not silently trusted; the till stops and says the shop's key has
/// changed, which is either a server that was rebuilt or something that should be looked at.
/// </para>
///
/// <para>
/// The fingerprint is device configuration and lives in this machine's own settings file, next
/// to the address it was paired with. Re-pairing is clearing it.
/// </para>
/// </summary>
public static class PinnedShop
{
    /// <summary>Set when a connection was refused because the shop's key is not the pinned one.</summary>
    public static string LastRefusal { get; private set; } = string.Empty;

    /// <summary>
    /// An HTTP client that talks to the paired shop and nothing else.
    /// </summary>
    public static HttpClient Client(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = Recognise,
        };

        return new HttpClient(handler) { Timeout = timeout };
    }

    private static bool Recognise(
        HttpRequestMessage request,
        X509Certificate2? certificate,
        X509Chain? chain,
        System.Net.Security.SslPolicyErrors errors)
    {
        if (certificate is null)
        {
            LastRefusal = "The shop offered no certificate.";
            return false;
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        var pinned = AppSettings.Current.ServerFingerprint.Trim();

        if (pinned.Length == 0)
        {
            // First meeting. This is the moment of trust, and it is the only one: from here the
            // till is paired with this machine and will not accept another without being told.
            AppSettings.Current.ServerFingerprint = fingerprint;
            AppSettings.Current.Save();
            LastRefusal = string.Empty;
            return true;
        }

        if (string.Equals(pinned, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            LastRefusal = string.Empty;
            return true;
        }

        // Deliberately not a question the user is asked mid-sale. A key that changed is either a
        // rebuilt server, which somebody knows about, or something that should not be there.
        LastRefusal =
            "This is not the shop this till was paired with: the server's key has changed. "
            + "If the shop's server was rebuilt, clear the paired key in Settings and connect "
            + "again. If it was not, something else is answering on that address.";

        return false;
    }
}
