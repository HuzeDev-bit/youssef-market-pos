<#
    Sets up the shop's server machine — Computer 1, the one that holds the database.

    Two things a shop cannot be expected to do by hand every morning:

      1. Let the tills through the firewall. Windows blocks incoming connections on port 5000
         by default, so the server answers perfectly on its own machine and is invisible to
         every till in the shop. That failure looks exactly like a broken cable.

      2. Start the server after a power cut. A shop that has to find a shortcut and
         double-click it before it can sell is a shop that will one morning forget.

    Run once, on the server machine, as Administrator:

        powershell -ExecutionPolicy Bypass -File tools\setup-shop-server.ps1

    Undo it with -Remove.
#>

[CmdletBinding()]
param(
    [string] $ServerExe = "$PSScriptRoot\..\dist\ShopServer\MarketPos.Server.exe",
    [int]    $Port = 5000,
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'
$ruleName = 'Market POS server'
$taskName = 'Market POS server'

function Test-Admin {
    $me = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $me).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host "This needs Administrator: right-click PowerShell and Run as administrator." -ForegroundColor Yellow
    exit 1
}

if ($Remove) {
    Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    schtasks /Delete /TN $taskName /F 2>$null | Out-Null
    Write-Host "Removed the firewall rule and the startup task." -ForegroundColor Green
    exit 0
}

$ServerExe = (Resolve-Path $ServerExe).Path
Write-Host "Server: $ServerExe"

# ---------------------------------------------------------------- the tills can reach it
#
# Private networks only. A shop's router is a private network; opening this on a public one
# would put the shop's takings on whatever coffee-shop Wi-Fi the machine joins next.
if (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue) {
    Write-Host "Firewall rule already present." -ForegroundColor DarkGray
} else {
    New-NetFirewallRule -DisplayName $ruleName `
        -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port `
        -Profile Private -Program $ServerExe | Out-Null
    Write-Host "Firewall opened on TCP $Port for private networks." -ForegroundColor Green
}

# ---------------------------------------------------------------- it starts by itself
#
# At logon of this user, not as a Windows service running as SYSTEM. The database lives under
# the shop's own AppData, and a service running as SYSTEM would open a different, empty one —
# a shop that boots to an empty catalogue and cannot tell why. Logon it is, with the machine
# set to sign in automatically if the owner wants no keyboard in the morning.
$who = "$env:USERDOMAIN\$env:USERNAME"

schtasks /Create /TN $taskName /TR "`"$ServerExe`"" /SC ONLOGON /RU $who /RL HIGHEST /F | Out-Null
Write-Host "Startup task created: runs at logon of $who." -ForegroundColor Green

# ---------------------------------------------------------------- what to tell the tills
Write-Host ""
Write-Host "Point each till at one of these:" -ForegroundColor Cyan
Write-Host "    http://$($env:COMPUTERNAME):$Port      (by name - survives the router changing its mind)"

Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    ForEach-Object { Write-Host "    http://$($_.IPAddress):$Port" }

Write-Host ""
Write-Host "Give this machine a fixed address on the router if you use the IP form," -ForegroundColor DarkGray
Write-Host "or the tills will be pointing at an address that moves." -ForegroundColor DarkGray
