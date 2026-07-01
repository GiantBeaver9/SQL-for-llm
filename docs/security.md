# Security & hardening

The Tailscale approach is secure by default (nothing exposed to the internet,
end-to-end encrypted, authenticated devices). Here's how to tighten it further.

## Baseline (already handled)

- **No public exposure.** RDP/SSH/SMB firewall rules are scoped to the
  Tailscale CGNAT range `100.64.0.0/10`, so only your Tailnet peers can reach
  them. Do **not** port-forward 3389/22/445 on your router.
- **Network Level Authentication** is required for RDP (`windows-setup.ps1`
  sets `UserAuthentication = 1`).
- **WireGuard encryption** for all traffic between devices.

## Recommended next steps

### 1. Strong account + MFA
Your Tailscale identity is the front door. Enable MFA on the identity provider
you sign in with (Google/GitHub/Microsoft). Anyone who gets into that account
can add a device to your Tailnet.

### 2. Key expiry
Leave **key expiry enabled** (default) so a lost/compromised device drops off
the Tailnet automatically. Review devices at
<https://login.tailscale.com/admin/machines> and remove ones you don't
recognize.

### 3. Tailscale ACLs (least privilege)
By default every device can reach every other device on all ports. Lock this
down in the admin console → **Access Controls**. Example: only your laptop and
phone may reach the PC, and only on the ports you use.

```jsonc
{
  "tagOwners": { "tag:home": ["your-email@example.com"] },
  "acls": [
    {
      // laptop + phone -> home PC, only RDP/SSH/SMB/web
      "action": "accept",
      "src":    ["your-email@example.com"],
      "dst":    ["tag:home:3389,22,445,8080"]
    }
  ]
}
```
Tag the PC with `tag:home` (`tailscale up --advertise-tags=tag:home`) so the
rule applies.

### 4. SSH keys, not passwords
Use key-based SSH auth and disable password auth on the PC's `sshd_config`
(`PasswordAuthentication no`). Consider **Tailscale SSH**
(`tailscale up --ssh`) which lets Tailscale broker SSH auth using your Tailnet
identity — no separate keys to manage, and it's governed by the same ACLs.

### 5. Windows account hygiene
- Use a **strong password** on the Windows account you RDP into (NLA still
  relies on it).
- Prefer a standard (non-admin) account for day-to-day remote use.
- Keep Windows and Tailscale updated.

### 6. Device approval (optional, stricter)
Enable **device approval** in the admin console so a newly-added device can't
join the Tailnet until you approve it — useful defense if your identity
provider is ever phished.

## Things to avoid

- ❌ Forwarding RDP (3389) on your router "as a backup." That reintroduces the
  exact internet exposure this setup exists to prevent.
- ❌ Disabling NLA to make an old client connect. Update the client instead.
- ❌ Sharing your Tailscale login. Use Tailnet **sharing** or ACLs to grant
  scoped access to others.

## Quick audit checklist

- [ ] MFA on the Tailscale identity provider
- [ ] Key expiry enabled; unknown devices removed
- [ ] Router does **not** forward 3389/22/445
- [ ] Firewall rules scoped to `100.64.0.0/10` (run the setup script)
- [ ] SSH password auth disabled (or using Tailscale SSH)
- [ ] Strong Windows account password, NLA on
- [ ] ACLs restrict who/what can reach the PC (optional but recommended)
