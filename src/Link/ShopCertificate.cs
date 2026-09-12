using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MarketPos.Link;

/// <summary>
/// The shop's own certificate, so a password never crosses the counter in the clear.
///
/// <para>
/// A shop has no certificate authority and no public name to get one for. What it has is one
/// machine that is the shop, and a handful of tills that should only ever talk to that machine.
/// So the shop makes its own certificate once, keeps it, and each till remembers the fingerprint
/// of the machine it was paired with. After that a till will talk to that server and no other:
/// not to a laptop that has taken its address, and not to anything sitting in between.
/// </para>
///
/// <para>
/// This is deliberately small. It is not a certificate authority and does not pretend to be
/// one. What it buys is the thing that matters here: the owner's password and every session
/// token are encrypted on the wire, and a server that is not the one the till was paired with
/// is refused rather than quietly trusted.
/// </para>
///
/// <para>
/// Replacing it: delete <c>server.pfx</c> from the shop's folder and restart the server, which
/// makes a new one. Every till then refuses to connect until it is re-paired — clear its
/// remembered fingerprint in Settings — which is exactly the noise a changed server key should
/// make.
/// </para>
/// </summary>
public static class ShopCertificate
{
    private static string Folder => System.IO.Path.GetDirectoryName(Data.Database.Path)!;

    private static string Pfx => System.IO.Path.Combine(Folder, "server.pfx");

    /// <summary>
    /// The certificate this shop serves with, made on first run and kept afterwards.
    ///
    /// Named for the machine and every address it answers on, so a till connecting by hostname
    /// and a till connecting by IP both see a name that matches. A certificate that is right
    /// for one and wrong for the other would be a certificate somebody turns validation off to
    /// get past.
    /// </summary>
    public static X509Certificate2 Ours()
    {
        if (File.Exists(Pfx))
        {
            var kept = new X509Certificate2(Pfx, (string?)null, X509KeyStorageFlags.Exportable);

            // A certificate that has run out is no better than none, and one that does not
            // carry the name the tills ask for is worse: it makes every till refuse the shop
            // for a reason nobody can see. Either way, make another.
            if (kept.NotAfter > DateTime.Now.AddDays(7) && Answers(kept, ShopName)) return kept;
        }

        var made = Make();
        File.WriteAllBytes(Pfx, made.Export(X509ContentType.Pfx));
        return made;
    }

    /// <summary>
    /// Whether a certificate carries a given name, so an old one made before the shop had a
    /// name can be spotted and replaced rather than served to tills that will refuse it.
    /// </summary>
    private static bool Answers(X509Certificate2 certificate, string name)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != "2.5.29.17") continue;   // subject alternative name

            if (extension.Format(true).Contains(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>The fingerprint a till pins. Printed by the server so it can be read out loud.</summary>
    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    /// <summary>
    /// What the server machine is called on the shop's network. The same name the tills ask
    /// for; kept here as a plain string because this file cannot see the till's own code.
    /// </summary>
    private const string ShopName = "pos-server";

    private static X509Certificate2 Make()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={Dns.GetHostName()}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));   // server authentication

        // Every name a till might reasonably use to reach this machine.
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(Dns.GetHostName());
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);

        // And the name the tills actually ask for. A certificate that does not carry the name
        // in the address is refused by the till before a single byte of shop data crosses, so
        // this has to be here whether or not the machine has been renamed yet -- otherwise a
        // shop that renames its server afterwards has to re-pair every till to fix it.
        if (!string.Equals(Dns.GetHostName(), ShopName, StringComparison.OrdinalIgnoreCase))
            names.AddDnsName(ShopName);

        foreach (var address in LocalAddresses()) names.AddIpAddress(address);

        request.CertificateExtensions.Add(names.Build());

        // Long enough that a shop is not re-pairing its tills every year, short enough to be a
        // key with an end.
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));

        // Round-tripped through a PFX so the private key is usable by Kestrel on Windows.
        return new X509Certificate2(
            certificate.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
    }

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
}
