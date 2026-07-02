#!/usr/bin/env bash
#
# connect.sh — convenience wrapper to reach your home PC over Tailscale
# from a Linux/macOS client.
#
# Usage:
#   ./connect.sh ssh                 # open a terminal (default)
#   ./connect.sh rdp                 # open a Remote Desktop session (GUI)
#   ./connect.sh files               # mount SMB / open sftp
#   ./connect.sh web <port>          # open http://<pc>:<port> in a browser
#   ./connect.sh ip                  # just print the PC's Tailscale IP
#   ./connect.sh wake [--wait]       # wake a sleeping PC via the Pi (WoL)
#
# Configure the target host once via env var or edit HOST below:
#   export REMOTE_PC=home-pc          # MagicDNS name, or 100.x.y.z IP
#   export REMOTE_USER=you
#   export WOL_HOST=pi-hole           # Pi's Tailscale name (for 'wake')
#   export WOL_USER=pi                # ssh user on the Pi (defaults to REMOTE_USER)
#
set -euo pipefail

HOST="${REMOTE_PC:-home-pc}"          # Tailscale MagicDNS name or IP
USER_NAME="${REMOTE_USER:-$USER}"
ACTION="${1:-ssh}"

# Prefer resolving through Tailscale if the CLI is present and the name is bare.
resolve_ip() {
  if command -v tailscale >/dev/null 2>&1; then
    tailscale ip -4 "$HOST" 2>/dev/null | head -n1 || echo "$HOST"
  else
    echo "$HOST"
  fi
}

open_url() {
  local url="$1"
  if command -v xdg-open >/dev/null 2>&1; then xdg-open "$url"
  elif command -v open  >/dev/null 2>&1; then open "$url"
  else echo "Open manually: $url"; fi
}

case "$ACTION" in
  ssh)
    exec ssh "${USER_NAME}@${HOST}"
    ;;
  rdp)
    ip="$(resolve_ip)"
    if command -v xfreerdp >/dev/null 2>&1; then
      exec xfreerdp "/v:${ip}" "/u:${USER_NAME}" /dynamic-resolution +clipboard
    elif command -v open >/dev/null 2>&1; then
      # macOS: hand off to Microsoft Remote Desktop.
      echo "Connect Microsoft Remote Desktop to: ${ip}  (user: ${USER_NAME})"
      open "rdp://full%20address=s:${ip}&username=s:${USER_NAME}" 2>/dev/null || true
    else
      echo "Install an RDP client (e.g. 'xfreerdp') then connect to ${ip}." >&2
      exit 1
    fi
    ;;
  files)
    echo "SMB share:   smb://${HOST}/   (or  \\\\${HOST}\\share  on Windows)"
    echo "SFTP:        sftp ${USER_NAME}@${HOST}"
    command -v sftp >/dev/null 2>&1 && exec sftp "${USER_NAME}@${HOST}"
    ;;
  web)
    port="${2:?usage: connect.sh web <port>}"
    open_url "http://${HOST}:${port}"
    ;;
  ip)
    resolve_ip
    ;;
  wake)
    pi="${WOL_HOST:?set WOL_HOST to the Pi Tailscale name}"
    piuser="${WOL_USER:-$USER_NAME}"
    echo "Waking ${HOST} via ${pi}..."
    ssh "${piuser}@${pi}" wake-home-pc
    if [[ "${2:-}" == "--wait" ]]; then
      printf 'Waiting for %s to respond' "$HOST"
      for _ in $(seq 1 30); do
        if ping -c1 -W1 "$HOST" >/dev/null 2>&1; then echo " up!"; exit 0; fi
        printf '.'; sleep 2
      done
      echo " (still no response after ~60s — give it a bit longer or check BIOS WoL)"
    fi
    ;;
  *)
    echo "Unknown action: $ACTION" >&2
    echo "Try: ssh | rdp | files | web <port> | ip | wake [--wait]" >&2
    exit 1
    ;;
esac
