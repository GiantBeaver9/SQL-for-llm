#!/usr/bin/env bash
#
# wake-pc.sh — send a Wake-on-LAN "magic packet" to wake the sleeping home PC.
# Runs on the always-on Raspberry Pi (Pi-hole box), or from anywhere via ssh.
#
# Usage:
#   ./wake-pc.sh                        # uses $PC_MAC (+ optional $LAN_BROADCAST)
#   ./wake-pc.sh AA:BB:CC:DD:EE:FF      # explicit MAC
#   LAN_BROADCAST=192.168.1.255 ./wake-pc.sh AA:BB:CC:DD:EE:FF
#
# WoL targets the MAC via a LAN broadcast, so the Pi must be on the SAME
# physical network as the PC (it is — that's why the Pi-hole box is perfect).
#
set -euo pipefail

MAC="${1:-${PC_MAC:-}}"
BROADCAST="${LAN_BROADCAST:-255.255.255.255}"

if [[ -z "$MAC" ]]; then
  echo "error: no MAC address. Set PC_MAC or pass it as an argument." >&2
  echo "usage: $0 <AA:BB:CC:DD:EE:FF>" >&2
  exit 1
fi

# Basic MAC sanity check.
if [[ ! "$MAC" =~ ^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$ ]]; then
  echo "error: '$MAC' does not look like a MAC address (AA:BB:CC:DD:EE:FF)." >&2
  exit 1
fi

echo "Sending magic packet to $MAC (broadcast $BROADCAST)..."
if command -v wakeonlan >/dev/null 2>&1; then
  wakeonlan -i "$BROADCAST" "$MAC"
elif command -v etherwake >/dev/null 2>&1; then
  sudo etherwake "$MAC"
elif command -v wol >/dev/null 2>&1; then
  wol "$MAC"
else
  echo "error: no WoL tool found. Install one:  sudo apt install -y wakeonlan" >&2
  exit 1
fi

echo "Sent. The PC should wake within ~10-30s and rejoin Tailscale."
echo "Then connect as usual (RDP/SSH/http) to its Tailscale name or IP."
