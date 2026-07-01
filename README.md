# Remote PC Connection

Securely connect to your home **Windows PC** from anywhere — full desktop
(RDP), terminal (SSH), files, and any app/service endpoint it hosts — without
exposing anything to the public internet.

## The approach: Tailscale (WireGuard mesh VPN)

Instead of forwarding ports on your router and exposing RDP to the internet
(a well-known attack magnet), we put your PC and your travel devices on a
private, encrypted mesh network with [Tailscale](https://tailscale.com). Once
both devices are signed into the same Tailnet, your laptop can reach the PC by
a stable name/IP **as if it were on the home LAN** — from any network, behind
any NAT, no port forwarding required.

```
┌─────────────┐        encrypted WireGuard tunnel        ┌──────────────┐
│ Laptop /    │  ───────────────────────────────────►    │ Home PC      │
│ phone       │        (via Tailscale coordination)      │ 100.x.y.z    │
│ (anywhere)  │  ◄───────────────────────────────────    │ RDP/SSH/etc. │
└─────────────┘                                           └──────────────┘
```

### Why this over the alternatives

| Option | Exposed to internet? | Setup | Good for |
|---|---|---|---|
| **Tailscale (this repo)** | No — private mesh | Easy, self-managed | Everything: RDP, SSH, files, endpoints |
| Router port-forward RDP | **Yes — risky** | Medium | Not recommended |
| Chrome Remote Desktop / TeamViewer | Via vendor cloud | Easiest | GUI only, less control |
| Raw WireGuard | No | Harder (manual keys/config) | Purists who want no third party |

Tailscale is free for personal use (up to 100 devices) and is the right fit
for "secure & self-managed."

## Quick start

1. **Create a Tailscale account** at <https://login.tailscale.com/start>
   (sign in with Google/GitHub/Microsoft). Do this once.

2. **On the home PC** (run PowerShell as Administrator):
   ```powershell
   ./scripts/windows-setup.ps1
   ```
   This installs Tailscale, enables Remote Desktop + the OpenSSH server,
   and adds the correct firewall rules **scoped to the Tailscale network only**.
   It finishes by printing the PC's Tailscale IP and name.

3. **On your laptop/phone**, install Tailscale and sign into the **same**
   account:
   - Windows/macOS/Linux: <https://tailscale.com/download>
   - iOS/Android: App Store / Play Store

4. **Connect** (see [`docs/remote-pc-setup.md`](docs/remote-pc-setup.md) for
   full details):
   - **RDP:** point Remote Desktop at the PC's Tailscale name (e.g. `home-pc`)
     or IP (`100.x.y.z`).
   - **SSH / terminal:** `ssh you@home-pc`
   - **Files:** `\\home-pc\...` (SMB) or `scp` / `sftp` over SSH.
   - **App endpoint:** `http://home-pc:PORT` — reach any service bound on the PC.

## What's in this repo

| Path | Purpose |
|---|---|
| [`docs/remote-pc-setup.md`](docs/remote-pc-setup.md) | Full step-by-step guide, every connection type, troubleshooting |
| [`docs/security.md`](docs/security.md) | Hardening: MagicDNS, ACLs, key expiry, disabling RDP-to-internet |
| [`scripts/windows-setup.ps1`](scripts/windows-setup.ps1) | One-shot host setup (Tailscale + RDP + SSH + firewall) |
| [`scripts/connect.sh`](scripts/connect.sh) | Convenience wrapper to RDP/SSH from a client |

## Important

- **Do not also port-forward RDP on your router.** The whole point is to keep
  RDP off the public internet. If you previously opened port 3389, close it.
- Keep the PC **awake** or configure Wake-on-LAN — a sleeping PC can't accept
  connections. See the troubleshooting section.
