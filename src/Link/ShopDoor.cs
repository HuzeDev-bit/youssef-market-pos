using System.Diagnostics;
using System.Security.Principal;

namespace MarketPos.Link;

/// <summary>
/// Opens the shop's door in Windows Firewall, so the other tills can actually reach it.
///
/// <para>
/// A server that binds every address on the machine is still invisible to every other computer
/// until Windows lets the traffic in, and Windows does not. What it does instead is show a
/// "Windows Security Alert" box the first time, once, and remember the answer against the exact
/// path the program was started from -- so a shop that dismissed that box, or that put a newer
/// copy of the server in a different folder, has a server which works perfectly on its own
/// machine and is unreachable from every till in the building. There is nothing on screen to
/// say so and nothing in a log.
/// </para>
///
/// <para>
/// So the rule made here is for the port rather than for the program: one rule, on every network
/// type, that keeps working when the exe is moved, renamed or replaced. TCP 5000 inbound and
/// nothing else -- not "allow this program everything", which is what the alert box grants.
/// </para>
///
/// <para>
/// It needs administrator rights, and a shop's computer usually does not run things as one. So
/// it says exactly what to do when it cannot, in the one place the owner is already looking.
/// </para>
/// </summary>
public static class ShopDoor
{
    public const string RuleName = "Market POS shop server";

    /// <summary>What happened, in words meant for whoever is setting the shop up.</summary>
    public sealed record Result(bool Open, string Said);

    /// <summary>
    /// Makes sure inbound TCP <paramref name="port"/> is allowed. Never throws: a firewall that
    /// cannot be read is a firewall that might already be fine, and the server must start either
    /// way rather than refusing to trade over a rule it could not check.
    /// </summary>
    public static Result Open(int port)
    {
        try
        {
            if (Already(port)) return new Result(true, "the firewall already lets tills in");

            var rule = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow "
                     + $"protocol=TCP localport={port} profile=any";

            if (AsAdministrator())
            {
                return Netsh(rule)
                    ? new Result(true, $"opened TCP {port} in Windows Firewall for the tills")
                    : new Result(false, NeedsAdmin(port));
            }

            // Not an administrator, which a shop's computer usually is not. Rather than print a
            // command for somebody to retype, ask Windows for the rights to do it -- one prompt,
            // once, on the machine whose owner is standing in front of it. Saying no is allowed
            // and leaves the instructions behind.
            if (Elevated(rule) && Already(port))
                return new Result(true, $"opened TCP {port} in Windows Firewall for the tills");

            return new Result(false, NeedsAdmin(port));
        }
        catch
        {
            return new Result(false, NeedsAdmin(port));
        }
    }

    /// <summary>
    /// The network this machine is on, as Windows has filed it.
    ///
    /// Worth saying out loud: on a network marked Public, Windows treats every other machine on
    /// it as a stranger. The rule above still works because it covers every profile, but a shop
    /// where nothing can find anything usually has this at the bottom of it.
    /// </summary>
    public static string Network()
    {
        try
        {
            var said = Run("powershell",
                "-NoProfile -Command \"(Get-NetConnectionProfile).NetworkCategory\"");

            var kinds = said.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 0)
                            .Distinct()
                            .ToList();

            return kinds.Count == 0 ? string.Empty : string.Join(", ", kinds);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Runs one netsh command as an administrator, through the prompt Windows shows for it.
    /// False when the person said no, or when there is nobody there to ask.
    /// </summary>
    private static bool Elevated(string arguments)
    {
        try
        {
            using var run = Process.Start(new ProcessStartInfo("netsh", arguments)
            {
                UseShellExecute = true,      // required for the prompt
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (run is null) return false;

            run.WaitForExit(30000);
            return run.HasExited && run.ExitCode == 0;
        }
        catch
        {
            // Cancelled at the prompt, or no desktop to show one on.
            return false;
        }
    }

    private static bool Already(int port)
    {
        var said = Netsh($"advfirewall firewall show rule name=\"{RuleName}\"", out var output);
        return said && output.Contains(port.ToString(), StringComparison.Ordinal);
    }

    private static bool AsAdministrator()
    {
        try
        {
            using var me = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string NeedsAdmin(int port) =>
        "the tills may not be able to reach this machine. To let them in, right-click Windows "
        + "PowerShell, choose \"Run as administrator\", and paste this one line:\r\n\r\n"
        + $"    netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow "
        + $"protocol=TCP localport={port} profile=any";

    private static bool Netsh(string arguments) => Netsh(arguments, out _);

    private static bool Netsh(string arguments, out string output)
    {
        output = Run("netsh", arguments);
        return output.Length > 0 && !output.Contains("No rules match", StringComparison.OrdinalIgnoreCase);
    }

    private static string Run(string program, string arguments)
    {
        using var run = new Process
        {
            StartInfo = new ProcessStartInfo(program, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        run.Start();
        var said = run.StandardOutput.ReadToEnd();
        run.WaitForExit(8000);
        return said;
    }
}
