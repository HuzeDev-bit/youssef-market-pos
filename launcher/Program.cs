using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ============================================================================
// MarketPos Launcher
//
// Double-click this to start both the back-office server and the POS frontend.
// When the till closes, the server is also shut down cleanly.
// ============================================================================
// Hide any console window — this is a GUI-only launcher.
// (OutputType=WinExe already suppresses the console, but belt-and-suspenders.)

var appDir = AppContext.BaseDirectory;
var serverExe = Path.Combine(appDir, "MarketPos.Server.exe");
var posExe    = Path.Combine(appDir, "MarketPos.exe");

// ── Sanity check ────────────────────────────────────────────────────────────
if (!File.Exists(serverExe) || !File.Exists(posExe))
{
    MessageBox.Show(
        $"ملفات التطبيق ناقصة.\n\nالمجلد: {appDir}\n\nتأكد من وجود:\n  MarketPos.Server.exe\n  MarketPos.exe",
        "MarketPos — خطأ",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    return 1;
}

// ── Start the server ─────────────────────────────────────────────────────────
var serverInfo = new ProcessStartInfo
{
    FileName               = serverExe,
    WorkingDirectory       = appDir,
    CreateNoWindow         = true,
    UseShellExecute        = false,
    // Keep logs tidy: server writes to its own console which we hide.
    RedirectStandardOutput = false,
    RedirectStandardError  = false,
};

Process? server = null;
try
{
    server = Process.Start(serverInfo)
        ?? throw new InvalidOperationException("Process.Start returned null.");
}
catch (Exception ex)
{
    MessageBox.Show(
        $"فشل تشغيل الخادم:\n{ex.Message}",
        "MarketPos — خطأ",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    return 1;
}

// ── Wait until the server is ready ──────────────────────────────────────────
// Poll /hello. The till does the same thing; if the server hasn't answered
// in 15 s something is genuinely broken and we tell the user.
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
var deadline = DateTime.UtcNow.AddSeconds(20);
var ready    = false;

while (DateTime.UtcNow < deadline)
{
    try
    {
        var response = await http.GetAsync("http://localhost:5000/hello");
        if (response.IsSuccessStatusCode) { ready = true; break; }
    }
    catch { /* still starting */ }

    await Task.Delay(400);
}

if (!ready)
{
    MessageBox.Show(
        "لم يستجب الخادم في الوقت المحدد.\nتأكد من أن المنفذ 5000 غير مستخدم.",
        "MarketPos — تحذير",
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning);
    // Continue anyway — the till has its own offline queue.
}

// ── Launch the POS frontend ──────────────────────────────────────────────────
using var pos = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName         = posExe,
        WorkingDirectory = appDir,
        UseShellExecute  = false,
    },
    EnableRaisingEvents = true,
};

pos.Start();
await pos.WaitForExitAsync();

// ── Shut down the server ─────────────────────────────────────────────────────
try
{
    if (server is { HasExited: false })
    {
        server.Kill(entireProcessTree: true);
        await server.WaitForExitAsync(CancellationToken.None);
    }
}
catch { /* already gone */ }

return 0;
