# Remote PC Setup — full guide

This walks through connecting to your home Windows PC from anywhere using
Tailscale, covering all four things you wanted: **full desktop (RDP)**,
**terminal (SSH)**, **files**, and **app/service endpoints**.

- [1. Concepts](#1-concepts)
- [2. Set up the host (home PC)](#2-set-up-the-host-home-pc)
- [3. Set up your client (laptop/phone)](#3-set-up-your-client-laptopphone)
- [4. Connect: RDP (full desktop)](#4-connect-rdp-full-desktop)
- [5. Connect: SSH (terminal)](#5-connect-ssh-terminal)
- [6. Connect: files](#6-connect-files)
- [7. Reach app / service endpoints](#7-reach-app--service-endpoints)
- [8. Troubleshooting](#8-troubleshooting)

---

## 1. Concepts

**Tailscale** builds a private mesh VPN ("Tailnet") on top of WireGuard. Every
device you sign in gets a stable address in the `100.64.0.0/10` range and,
with MagicDNS, a name like `home-pc`. Traffic between your devices is
end-to-end encrypted and travels directly (peer-to-peer) whenever possible,
falling back to Tailscale's relays only when NAT prevents a direct path.

The key security property: **your PC's services (RDP, SSH, SMB, web apps) are
only reachable by devices on your Tailnet.** They are never exposed to the LAN
or the public internet. That's why we don't touch your router.

---

## 2. Set up the host (home PC)

### Option A — automated (recommended)

1. Clone this repo (or just copy `scripts/windows-setup.ps1`) onto the PC.
2. Open **PowerShell as Administrator**.
3. Allow the script to run for this session and execute it:
   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   ./scripts/windows-setup.ps1
   ```
4. When the browser opens, sign into Tailscale (create the account first at
   <https://login.tailscale.com/start> if you haven't).
5. Note the printed **Tailscale IP** and **MagicDNS name**.

The script enables RDP + SSH + SMB, scopes their firewall rules to the
Tailscale network only, and stops the PC from sleeping on AC power. Flags:
`-NoSsh` and `-NoSmb` if you want fewer surfaces.

### Option B — manual

<details>
<summary>Click to expand manual steps</summary>

1. **Install Tailscale:** <https://tailscale.com/download/windows>, then
   `tailscale up`.
2. **Enable RDP:** Settings → System → Remote Desktop → On. (Requires Windows
   Pro/Enterprise. Home edition has no RDP host — use SSH + a GUI tool, or
   Chrome Remote Desktop for GUI.)
3. **Enable SSH:** Settings → System → Optional features → Add → *OpenSSH
   Server*. Then in an admin PowerShell:
   ```powershell
   Set-Service sshd -StartupType Automatic; Start-Service sshd
   ```
4. **Scope firewall to Tailscale** (admin PowerShell):
   ```powershell
   New-NetFirewallRule -DisplayName 'RDP over Tailscale' -Direction Inbound `
     -Action Allow -Protocol TCP -LocalPort 3389 -RemoteAddress 100.64.0.0/10
   ```
   and disable the broad "Remote Desktop" group rules.

</details>

---

## 3. Set up your client (laptop/phone)

1. Install Tailscale: <https://tailscale.com/download>
   (desktop) or the App Store / Play Store (mobile).
2. Sign into the **same account** as the PC.
3. Verify the PC shows up:
   ```bash
   tailscale status        # you should see 'home-pc' listed
   ```

On Linux/macOS you can use the helper: `export REMOTE_PC=home-pc REMOTE_USER=you`
then `./scripts/connect.sh ssh|rdp|files|web`.

---

## 4. Connect: RDP (full desktop)

- **Windows client:** open **Remote Desktop Connection** (`mstsc`), enter the
  PC's MagicDNS name (`home-pc`) or Tailscale IP (`100.x.y.z`), connect with
  your Windows username/password.
  ```
  mstsc /v:home-pc
  ```
- **macOS:** install *Microsoft Remote Desktop* from the App Store, add a PC
  with the Tailscale name/IP.
- **iOS/Android:** *Microsoft Remote Desktop* app, same idea.
- **Linux:** `xfreerdp /v:home-pc /u:YOU /dynamic-resolution +clipboard`
  (or `./scripts/connect.sh rdp`).

RDP gives you the real desktop and logs you into the session — it's the "sit at
the PC" experience.

---

## 5. Connect: SSH (terminal)

```bash
ssh you@home-pc
# or
ssh you@100.x.y.z
```

For passwordless login, copy your public key to the PC. On Windows the
authorized keys live at `C:\Users\<you>\.ssh\authorized_keys` (or, for admin
accounts, `C:\ProgramData\ssh\administrators_authorized_keys`).

```bash
ssh-copy-id you@home-pc            # if available
# or manually append your ~/.ssh/id_ed25519.pub to authorized_keys
```

You now have a shell for commands, editing, and running things headless.

---

## 6. Connect: files

- **SMB (drag-and-drop in Explorer/Finder):**
  - Windows: `\\home-pc\SharedFolder`
  - macOS/Linux: `smb://home-pc/SharedFolder`
  - First share a folder on the PC: right-click → Properties → Sharing.
- **Over SSH (no share needed):**
  ```bash
  scp report.pdf you@home-pc:Documents/     # copy up
  scp you@home-pc:Documents/report.pdf .    # copy down
  sftp you@home-pc                          # interactive
  ```
- **Tailscale Taildrop** (quick file send between your own devices):
  ```bash
  tailscale file cp report.pdf home-pc:
  ```

---

## 7. Reach app / service endpoints

Any service listening on the PC is reachable at its Tailscale address — no
extra config beyond making sure the app binds to an interface Tailscale can
reach (bind to `0.0.0.0`, not only `127.0.0.1`, if you can't reach it).

```
http://home-pc:8080          # a local web app / dashboard
http://100.x.y.z:5432        # e.g. a database port (use a real DB client)
```

Two nice extras:

- **MagicDNS names** make these URLs stable even if the IP changes.
- **Tailscale Serve** can put a service behind HTTPS with a valid cert inside
  your Tailnet:
  ```bash
  tailscale serve https / http://localhost:8080
  ```
  Then browse `https://home-pc.<your-tailnet>.ts.net`.

> If a service binds only to `localhost`, you can still reach it by SSH-ing in
> and using it locally, or by SSH port-forwarding:
> `ssh -L 8080:localhost:8080 you@home-pc` then open `http://localhost:8080`
> on your laptop.

---

## 8. Troubleshooting

| Symptom | Fix |
|---|---|
| PC not in `tailscale status` | Make sure it's signed into the **same** account and `tailscale up` ran. |
| Can ping but RDP refused | Confirm RDP is enabled and the "RDP over Tailscale" firewall rule exists; Windows **Home** has no RDP host. |
| Works then dies after a while | PC went to sleep. The setup script disables AC standby; also see Wake-on-LAN below. |
| Endpoint unreachable but SSH works | The app is bound to `127.0.0.1` only. Rebind to `0.0.0.0`, or use SSH port-forwarding. |
| Slow / high latency | You may be on a relay (DERP). Check `tailscale ping home-pc` — "direct" is good, "via DERP" means NAT is blocking p2p. |
| Want it locked down further | See [`security.md`](security.md) — ACLs, key expiry, tagged devices. |

### Waking a sleeping PC (optional)

If you can't keep the PC always-on, enable **Wake-on-LAN** in BIOS and the
NIC's power settings, then use a second always-on Tailnet device (e.g. a
Raspberry Pi or the router if it runs Tailscale) to send the magic packet.
Simplest alternative: set the PC to never sleep on AC power (the setup script
already does this).
