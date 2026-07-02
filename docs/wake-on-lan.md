# Wake-on-LAN with the Pi-hole Raspberry Pi

Goal: **let the PC sleep when you're away (~2W) and wake it on demand** using
your always-on Raspberry Pi as the wake relay. Best of both worlds — low power
*and* access.

## How it works

```
  You (anywhere)                         Home LAN
 ┌────────────┐   Tailnet   ┌──────────────┐   magic packet   ┌──────────────┐
 │ laptop/    │ ──────────► │ Raspberry Pi │ ───────────────► │ PC (asleep)  │
 │ phone      │  "wake!"    │ (Pi-hole,    │   (LAN broadcast │ wakes, ~10s, │
 │            │             │  always on)  │    to PC's MAC)  │ rejoins Tailnet│
 └────────────┘             └──────────────┘                  └──────────────┘
        │                                                             ▲
        └──────────────── RDP / SSH / http once awake ────────────────┘
```

The Pi is already on and on your LAN, so it can broadcast the Wake-on-LAN
"magic packet" to the PC's MAC address. You reach the Pi from anywhere over
Tailscale, tell it to wake the PC, then connect to the PC directly once it's up.

## Do I need a static IP?

Short version: **no static *public* IP** (Tailscale handles your changing home
IP), and the wake itself targets the **MAC**, not an IP. But a **DHCP
reservation** for the PC and the Pi is worth doing for reliability — and since
Pi-hole can be your DHCP server, it's a couple of clicks:

- Pi-hole admin → **Settings → DHCP** → enable DHCP (if you use it) →
  **Add static DHCP lease** for the PC and the Pi (bind their MACs to fixed IPs).

## Setup

### 1. On the PC (elevated PowerShell)

Run the host setup in **Wake-on-LAN mode** so it sleeps and accepts wake
packets, instead of staying on 24/7:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
./scripts/windows-setup.ps1 -WakeOnLan            # optionally -SleepAfterMinutes 20
```

This:
- enables **Wake-on-Magic-Packet** on the wired NIC,
- disables **Fast Startup** (so WoL works from shutdown too),
- sets the PC to **sleep after 30 idle minutes** (tune with `-SleepAfterMinutes`),
- prints the PC's **MAC address** — copy it for the next step.

Then, one manual step the script can't do: **enable "Wake on LAN" / "Power On
By PCIe/PCI" in your BIOS/UEFI.** Reboot into firmware setup, find it under
Power Management, enable it, save.

> ⚠️ **Use wired Ethernet.** Wi-Fi Wake-on-LAN (WoWLAN) is unreliable and often
> unsupported. If the PC is on Wi-Fi, run a cable or use scheduled wake instead.

### 2. On the Pi (the Pi-hole box)

Copy this repo's `scripts/` folder to the Pi (or just `wake-pc.sh` +
`pi-wol-setup.sh`), then:

```bash
sudo ./pi-wol-setup.sh AA:BB:CC:DD:EE:FF     # the MAC from step 1
```

This installs `wakeonlan`, ensures Tailscale is running (sign into the **same**
account as the PC), and installs a `wake-home-pc` command with the MAC baked in.

### 3. Wake + connect from anywhere

From any device on your Tailnet:

```bash
# wake it
ssh pi@pi-hole wake-home-pc

# ...or with the client helper (waits until it's reachable):
export REMOTE_PC=home-pc REMOTE_USER=you WOL_HOST=pi-hole WOL_USER=pi
./scripts/connect.sh wake --wait
./scripts/connect.sh rdp        # then connect
```

The PC wakes in ~10–30s, rejoins Tailscale, and you RDP/SSH/http to it as usual.

## Optional: one-tap wake from your phone

Put a tiny wake button behind Tailscale on the Pi so you can wake the PC from a
browser (no SSH). On the Pi:

```bash
# a minimal one-shot HTTP endpoint that runs wake-home-pc
# (install as a systemd service; requires python3, already on Raspberry Pi OS)
cat > ~/wake-server.py <<'PY'
from http.server import BaseHTTPRequestHandler, HTTPServer
import subprocess
class H(BaseHTTPRequestHandler):
    def do_GET(self):
        subprocess.run(["/usr/local/bin/wake-home-pc"])
        self.send_response(200); self.end_headers()
        self.wfile.write(b"Waking home PC...\n")
HTTPServer(("127.0.0.1", 8099), H).serve_forever()
PY
python3 ~/wake-server.py &        # run under systemd for real use
tailscale serve --bg 8099         # expose it at https://pi-hole.<tailnet>.ts.net
```

Then bookmark `https://pi-hole.<your-tailnet>.ts.net` on your phone — tap to wake.
(For anything permanent, run `wake-server.py` as a systemd service rather than
backgrounding it, and consider Tailscale ACLs so only your devices can hit it.)

## A note on "Modern Standby" (S0)

Some newer PCs use **S0 Modern Standby** instead of classic **S3 sleep**. On S0,
the machine keeps the network alive in low-power idle — which can mean it stays
on Tailscale even while "asleep," so you may not need WoL at all (just connect).
Check with:

```powershell
powercfg /a       # lists supported sleep states
```

If it shows "Standby (S3)", classic WoL applies (this guide). If it only shows
"Standby (S0 Low Power Idle)", try connecting while it's idle first — it may
already be reachable.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Magic packet sent, PC doesn't wake | BIOS WoL not enabled; or on Wi-Fi; or Fast Startup still on (re-run setup). |
| Wakes from sleep but not from full shutdown | Disable Fast Startup (setup does this) and confirm BIOS "Power On by PCIe". |
| `wake-home-pc: PC_MAC not set` | Re-run `pi-wol-setup.sh <MAC>`; it writes `/etc/remote-pc/config`. |
| Wakes but you can't connect for ~20s | Normal — Tailscale takes a few seconds to reconnect after resume. Use `connect.sh wake --wait`. |
| Pi can't reach PC's subnet | Ensure the Pi and PC are on the same LAN/VLAN; WoL broadcasts don't cross subnets by default. |
