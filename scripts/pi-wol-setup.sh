#!/usr/bin/env bash
#
# pi-wol-setup.sh — turn the always-on Raspberry Pi (your Pi-hole box) into the
# Wake-on-LAN relay for the home PC.
#
# It:
#   1. installs a WoL tool (wakeonlan),
#   2. installs Tailscale if it isn't already there (so you can reach the Pi
#      from anywhere), and brings it up,
#   3. installs the wake command as /usr/local/bin/wake-home-pc, with the PC's
#      MAC baked in — so waking the PC from anywhere is one command.
#
# Run ON THE PI:
#   sudo ./pi-wol-setup.sh AA:BB:CC:DD:EE:FF
# (the MAC is printed by windows-setup.ps1 -WakeOnLan on the PC)
#
set -euo pipefail

PC_MAC="${1:-${PC_MAC:-}}"

if [[ $EUID -ne 0 ]]; then
  echo "error: run with sudo:  sudo $0 <PC-MAC>" >&2
  exit 1
fi
if [[ -z "$PC_MAC" ]]; then
  echo "usage: sudo $0 <PC-MAC e.g. AA:BB:CC:DD:EE:FF>" >&2
  exit 1
fi
if [[ ! "$PC_MAC" =~ ^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$ ]]; then
  echo "error: '$PC_MAC' is not a valid MAC (AA:BB:CC:DD:EE:FF)." >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "==> Installing wakeonlan"
apt-get update -qq
apt-get install -y wakeonlan

echo "==> Ensuring Tailscale is installed"
if ! command -v tailscale >/dev/null 2>&1; then
  curl -fsSL https://tailscale.com/install.sh | sh
fi
# Bring Tailscale up (prints an auth URL the first time — sign into the SAME
# account as the PC).
tailscale up || echo "   (run 'sudo tailscale up' manually if authentication is needed)"

echo "==> Installing the wake command"
install -m 0755 "${SCRIPT_DIR}/wake-pc.sh" /usr/local/bin/wake-pc
install -d -m 0755 /etc/remote-pc
printf 'PC_MAC=%s\n' "$PC_MAC" > /etc/remote-pc/config
chmod 0644 /etc/remote-pc/config

cat > /usr/local/bin/wake-home-pc <<'EOF'
#!/usr/bin/env bash
# Wake the home PC using the MAC stored in /etc/remote-pc/config.
set -a; [ -f /etc/remote-pc/config ] && . /etc/remote-pc/config; set +a
exec /usr/local/bin/wake-pc "${PC_MAC:?PC_MAC not set in /etc/remote-pc/config}"
EOF
chmod 0755 /usr/local/bin/wake-home-pc

echo
echo "Done. PC MAC stored: $PC_MAC"
echo
echo "Wake the PC from any Tailnet device with:"
echo "    ssh <pi-user>@<pi-tailscale-name> wake-home-pc"
echo
echo "This Pi's Tailscale name/IP:"
tailscale status --self --peers=false 2>/dev/null || tailscale ip -4 2>/dev/null || true
echo
echo "Tip: reserve the PC's (and this Pi's) IP in Pi-hole's DHCP for reliability."
