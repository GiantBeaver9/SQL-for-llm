#Requires -RunAsAdministrator
<#
.SYNOPSIS
    One-shot setup for secure remote access to this Windows PC via Tailscale.

.DESCRIPTION
    Installs Tailscale and enables the connection surfaces you asked for:
      - Remote Desktop (RDP)      -> full GUI
      - OpenSSH Server            -> terminal + file transfer (scp/sftp)
      - File & Printer Sharing    -> SMB file access (\\host\share)

    All firewall rules are scoped to the Tailscale interface / CGNAT range
    (100.64.0.0/10) so these services are NOT exposed to your LAN or the
    public internet -- only to devices on your Tailnet.

.NOTES
    Run in an elevated PowerShell:  Right-click > "Run as administrator".
#>

[CmdletBinding()]
param(
    # Skip enabling the OpenSSH server if you only want RDP.
    [switch]$NoSsh,
    # Skip opening SMB (file sharing) on the Tailscale interface.
    [switch]$NoSmb,
    # Let the PC sleep and wake via Wake-on-LAN (magic packet) instead of
    # staying on 24/7. Pair this with an always-on device (e.g. your Pi-hole
    # Raspberry Pi) that sends the wake packet -- see scripts/pi-wol-setup.sh.
    [switch]$WakeOnLan,
    # When -WakeOnLan is set, sleep after this many idle minutes on AC power.
    [int]$SleepAfterMinutes = 30
)

$ErrorActionPreference = 'Stop'
$TailscaleCidr = '100.64.0.0/10'   # Tailscale CGNAT range (RFC 6598)

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "    [ok] $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    [!]  $msg" -ForegroundColor Yellow }

# --- 0. Sanity check -------------------------------------------------------
if (-not ([Security.Principal.WindowsPrincipal] `
        [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run as Administrator.'
}

# --- 1. Install Tailscale --------------------------------------------------
Write-Step 'Installing Tailscale'
if (Get-Command tailscale.exe -ErrorAction SilentlyContinue) {
    Write-Ok 'Tailscale already installed.'
} elseif (Get-Command winget -ErrorAction SilentlyContinue) {
    winget install --exact --id Tailscale.Tailscale `
        --accept-source-agreements --accept-package-agreements
    Write-Ok 'Installed via winget.'
} else {
    $installer = Join-Path $env:TEMP 'tailscale-setup.exe'
    Write-Warn2 'winget not found; downloading MSI installer directly.'
    Invoke-WebRequest -Uri 'https://pkgs.tailscale.com/stable/tailscale-setup-latest.exe' `
        -OutFile $installer
    Start-Process -FilePath $installer -ArgumentList '/quiet' -Wait
    Write-Ok 'Installed via direct download.'
}

# Make sure tailscale.exe is on PATH for the rest of this session.
$tsDir = 'C:\Program Files\Tailscale'
if (Test-Path $tsDir) { $env:Path = "$tsDir;$env:Path" }

# --- 2. Enable Remote Desktop (RDP) ---------------------------------------
Write-Step 'Enabling Remote Desktop (RDP)'
Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server' `
    -Name 'fDenyTSConnections' -Value 0
# Require Network Level Authentication (more secure handshake).
Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp' `
    -Name 'UserAuthentication' -Value 1
Write-Ok 'RDP enabled with Network Level Authentication.'

Write-Step 'Restricting RDP firewall rule to the Tailscale network'
# Disable the broad built-in RDP rules, then add a tight Tailscale-only rule.
Get-NetFirewallRule -DisplayGroup 'Remote Desktop' -ErrorAction SilentlyContinue |
    Set-NetFirewallRule -Enabled False
if (-not (Get-NetFirewallRule -DisplayName 'RDP over Tailscale' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'RDP over Tailscale' -Direction Inbound `
        -Action Allow -Protocol TCP -LocalPort 3389 -RemoteAddress $TailscaleCidr | Out-Null
}
Write-Ok 'RDP reachable only from Tailscale peers (port 3389).'

# --- 3. Enable OpenSSH Server ---------------------------------------------
if (-not $NoSsh) {
    Write-Step 'Installing & enabling OpenSSH Server'
    $ssh = Get-WindowsCapability -Online -Name 'OpenSSH.Server*'
    if ($ssh.State -ne 'Installed') {
        Add-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' | Out-Null
    }
    Set-Service -Name sshd -StartupType Automatic
    Start-Service sshd
    # Replace the default open SSH firewall rule with a Tailscale-scoped one.
    Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue |
        Set-NetFirewallRule -Enabled False
    if (-not (Get-NetFirewallRule -DisplayName 'SSH over Tailscale' -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName 'SSH over Tailscale' -Direction Inbound `
            -Action Allow -Protocol TCP -LocalPort 22 -RemoteAddress $TailscaleCidr | Out-Null
    }
    Write-Ok 'SSH server running, reachable only from Tailscale peers (port 22).'
} else {
    Write-Warn2 'Skipping SSH server (-NoSsh).'
}

# --- 4. File sharing (SMB) over Tailscale ---------------------------------
if (-not $NoSmb) {
    Write-Step 'Allowing SMB file sharing over Tailscale'
    if (-not (Get-NetFirewallRule -DisplayName 'SMB over Tailscale' -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName 'SMB over Tailscale' -Direction Inbound `
            -Action Allow -Protocol TCP -LocalPort 445 -RemoteAddress $TailscaleCidr | Out-Null
    }
    Write-Ok 'SMB (\\host\share) reachable from Tailscale peers (port 445).'
    Write-Warn2 'You still need to share a folder in Explorer (right-click > Properties > Sharing).'
} else {
    Write-Warn2 'Skipping SMB (-NoSmb).'
}

# --- 5. Power: Wake-on-LAN (save power) or stay awake ---------------------
if ($WakeOnLan) {
    Write-Step "Configuring Wake-on-LAN (sleep after $SleepAfterMinutes min, wake on a magic packet)"

    # Disable Fast Startup so WoL works reliably from shutdown as well as sleep.
    Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' `
        -Name 'HiberbootEnabled' -Value 0 -ErrorAction SilentlyContinue
    Write-Ok 'Fast Startup disabled (WoL reliable from sleep and shutdown).'

    # Enable magic-packet wake on every active wired adapter.
    $wolSet = $false
    foreach ($ad in (Get-NetAdapter -Physical -ErrorAction SilentlyContinue |
                     Where-Object Status -eq 'Up')) {
        try {
            Set-NetAdapterPowerManagement -Name $ad.Name -WakeOnMagicPacket Enabled `
                -DeviceSleepOnDisconnect Disabled -ErrorAction Stop
            # Some NIC drivers also gate this behind an advanced property.
            Get-NetAdapterAdvancedProperty -Name $ad.Name -ErrorAction SilentlyContinue |
                Where-Object { $_.DisplayName -match 'Wake on Magic Packet|WakeOnMagicPacket' } |
                ForEach-Object {
                    Set-NetAdapterAdvancedProperty -Name $ad.Name `
                        -DisplayName $_.DisplayName -DisplayValue 'Enabled' -ErrorAction SilentlyContinue
                }
            Write-Ok "Wake-on-Magic-Packet enabled on '$($ad.Name)'."
            $wolSet = $true
        } catch {
            Write-Warn2 "Could not set WoL on '$($ad.Name)': $($_.Exception.Message)"
        }
    }
    if (-not $wolSet) {
        Write-Warn2 'No wired adapter accepted WoL settings -- check the NIC driver.'
    }

    powercfg /change standby-timeout-ac $SleepAfterMinutes
    Write-Ok "PC will sleep after $SleepAfterMinutes idle minutes on AC power."
    Write-Warn2 'ALSO enable "Wake on LAN" / "Power On by PCIe" in your BIOS/UEFI.'
    Write-Warn2 'WoL needs WIRED Ethernet -- Wi-Fi wake is unreliable.'
} else {
    Write-Step 'Preventing sleep so the PC stays reachable'
    powercfg /change standby-timeout-ac 0   # never sleep on AC power
    Write-Ok 'Standby on AC power disabled. Re-run with -WakeOnLan to sleep & save power.'
}

# --- 6. Bring Tailscale up -------------------------------------------------
Write-Step 'Starting Tailscale (a browser window will open to authenticate)'
try {
    & tailscale up
} catch {
    Write-Warn2 "Run 'tailscale up' manually to sign in: $_"
}

# --- 7. Report -------------------------------------------------------------
Write-Step 'Done. Connection details:'
try {
    $ip4  = (& tailscale ip -4) 2>$null
    $name = (& tailscale status --json | ConvertFrom-Json).Self.DNSName.TrimEnd('.')
    $mac  = (Get-NetAdapter -Physical -ErrorAction SilentlyContinue |
             Where-Object Status -eq 'Up' | Select-Object -First 1).MacAddress
    Write-Host ""
    Write-Host "    Tailscale IP  : $ip4"          -ForegroundColor Green
    Write-Host "    MagicDNS name : $name"          -ForegroundColor Green
    if ($WakeOnLan) {
        Write-Host "    MAC (for WoL) : $mac"        -ForegroundColor Green
        Write-Host "    -> Give this MAC to the Pi:  sudo ./pi-wol-setup.sh $mac" -ForegroundColor White
    }
    Write-Host ""
    Write-Host "    From another Tailnet device:"   -ForegroundColor White
    Write-Host "      RDP  : mstsc /v:$ip4"          -ForegroundColor White
    Write-Host "      SSH  : ssh $env:USERNAME@$ip4" -ForegroundColor White
    Write-Host "      Web  : http://$ip4:<port>"     -ForegroundColor White
} catch {
    Write-Warn2 "Run 'tailscale ip -4' and 'tailscale status' to see this PC's address."
}
Write-Host "`n    Next: install Tailscale on your laptop/phone and sign into the SAME account." -ForegroundColor Cyan
