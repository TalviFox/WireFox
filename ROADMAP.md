# WireFox Future Architecture & Feature Roadmap (v1.0.7+)

This document tracks forward-looking architectural proposals, UI improvements, and feature expansions planned beyond the immediate **v1.0.6** stabilization release.

---

## 1. Release Timeline & SemVer Alignment

| Milestone | Target Scope | Focus |
|---|---|---|
| **v1.0.6** | Core Stability & Reliability | Watchdog teardown fixes, graceful service unbinding, single-instance IPC, tray restore reliability. |
| **v1.0.7** | Touchscreen & Diagnostics Polish | Touch hitboxes (44px min), touch scrolling, gesture support, offline-aware diagnostic scan toasts. |
| **v1.1.0** | Ephemeral WFP Security Engine | Opt-in "Block All Traffic (Kill Switch)" action, zero persistent lockup guarantee, dynamic WFP sessions. |
| **v2.0.0+** | "Crazy Enough to Work" | Native Tunnel & Peer QR Studio, TOTP (2FA) Gated Tunnels, Fail-Closed Pre-Tunnel Lockdown. |

---

## 2. Touchscreen & Tablet UX Optimization (Target: v1.0.7)

### Problem Statement
On 2-in-1 Windows devices (Surface Pro, ThinkPad Yoga, Dell XPS) running in tablet or touch mode:
- Standard desktop controls (e.g. 24px icon buttons, compact combo boxes) are difficult to tap accurately without a trackpad/mouse.
- Horizontal scrollbars on live log boxes require precise mouse drag.
- Swipe gestures for switching pages or clearing notifications are not supported natively by standard desktop WPF views without specialized touch handlers.

### Proposed Enhancements
1. **Touch-Friendly Hitboxes:**
   - Enforce a minimum touch target size of 44x44 DPI across all primary interactive controls (`ToggleSwitch`, `Button`, `NavView` items).
   - Add touch padding/margin overrides when Windows tablet mode is active (`SM_CONVERTIBLESLATEMODE` / `SM_TABLETPC`).
2. **Side-Scroll vs Wrap Toggles in Log Viewers:**
   - Introduce an interactive touch-friendly toggle button in [DiagnosticsPage.xaml](file:///c:/Users/Talvi/Documents/IDE-Scratch/WireFox/Views/DiagnosticsPage.xaml) allowing users to switch between "Soft Wrap" and "Side-Scroll with Inertia".
3. **Touch Inertia & Manipulation:**
   - Enable `ScrollViewer.PanningMode="Both"` and `ManipulationBoundaryFeedback` on data cards and log lists to support fluid finger drag.

---

## 3. Ephemeral "Pseudo Kill Switch" on Tunnel Freeze (Target: v1.1.0)

### Current Architecture in Settings
[SettingsPage.xaml:L271](file:///c:/Users/Talvi/Documents/IDE-Scratch/WireFox/Views/SettingsPage.xaml#L271) defines the `Tunnel Freeze / Lockup Action` dropdown:
1. `Auto-Recover (Restart)` *(Default)*
2. `Pause Tunnel (15m)`
3. `Prompt Me (Toast)`

### Proposal: Opt-in "Block All Traffic (Kill Switch)"
Add a 4th action to the dropdown: **`Block All Traffic (Kill Switch)`**.

### The Golden Constraint: Zero Persistent Lockups Across Reboots
Poorly designed VPN kill switches are notorious for leaving permanent firewall rules or dead routes behind if Windows reboots, updates, or crashes—leaving the user stranded with "no internet" until they manually reset their network stack. WireFox's implementation must be **100% ephemeral**:

1. **Ephemeral Windows Filtering Platform (WFP) Sessions (`FWPM_SESSION_FLAG_DYNAMIC`):**
   - Block rules are registered in a dynamic WFP engine session bound strictly to the `WireFox.exe` process handle using `FwpmEngineOpen0` with `FWPM_SESSION_FLAG_DYNAMIC`.
   - If WireFox exits, crashes, or is killed in Task Manager, **the Windows kernel instantly and automatically vaporizes all dynamic block filters**.
   - Even with Windows 10/11 "Fast Startup" (kernel hibernation), because the user process is terminated on shutdown, the dynamic rules are never saved to `hiberfil.sys`.

2. **Non-Negotiable Startup "Sanity Sweeper" ([App.xaml.cs](file:///c:/Users/Talvi/Documents/IDE-Scratch/WireFox/App.xaml.cs)):**
   - As a belt-and-suspenders guarantee, on application boot before any watchdog timers begin, WireFox unconditionally runs a quick sweep:
     - Detects and clears any lingering `WireFox-KillSwitch` firewall tags or dead route metrics.
     - Guarantees that opening WireFox (or booting into Windows) immediately restores normal connectivity.

3. **User Flow & Value Proposition:**
   - Standard WireFox is an *automatic connectivity manager* (it keeps you connected).
   - A Kill Switch is an *anti-leak security tool* (it prefers zero internet over unprotected internet).
   - Placing it as an opt-in choice in the "Tunnel Freeze / Lockup Action" dropdown gives paranoid/privacy users a bulletproof kill switch without compromising the seamless experience for general users.

---

## 4. Offline State-Aware Diagnostic Notifications (Target: v1.0.7)

### Problem & Discovery during v1.0.6 Stabilization
WireGuard on Windows is fundamentally connectionless UDP. When physical Wi-Fi is toggled off or an Ethernet cable is detached, the Windows kernel service (`WireGuardTunnel$...`) and Wintun miniport adapter stay running by design so that WireGuard can instantly resume passing packets the millisecond a physical uplink returns without needing driver reinstallation or handshake renegotiation.

However, executing a manual **Diagnostic System Scan** while physical network adapters are offline reports `"All systems nominal (CLI ready, 1 active tunnel)"`, which can feel unintuitive to a user who has intentionally disabled their physical internet connection.

### Proposed Architecture & Enhancement
In [DiagnosticsViewModel.cs](file:///c:/Users/Talvi/Documents/IDE-Scratch/WireFox/ViewModels/DiagnosticsViewModel.cs), evaluate `!NetworkMonitorService.Instance.CurrentState.IsConnected` prior to declaring the system nominal:

- **Target Toast Content:**
  - Title: `Diagnostic Scan Notice`
  - Message: `"Network offline. WireGuard tunnel standing by."`
- **Universal Accuracy (Zero Guesswork):**
  - Universally valid regardless of whether Wi-Fi was toggled off, an Ethernet cable was detached, or Airplane Mode was enabled.
  - Avoids guessing or relying on stale previous interface cache states.
  - Confirms to the user that physical uplinks are down while reassuring them that the WireGuard kernel tunnel is healthy and standing by to resume routing immediately upon physical reconnect.

---

## 5. Distant Horizon: "Crazy Enough to Work" Features (Target: v2.0+)

### 5.1 Native Tunnel Creator & Mobile QR Studio ("AirDrop for WireGuard")
- **The Concept:** Eliminate the need to ever touch the upstream WireGuard client or manually hand-edit text configurations.
- **In-App Keypair Generation:** Generate Curve25519 public/private keypairs locally via .NET cryptography.
- **Visual Split-Tunnel Builder:** Intuitive UI toggles for AllowedIPs (`0.0.0.0/0` vs Split LAN), DNS server presets (Cloudflare, Quad9, AdGuard), and PersistentKeepalive.
- **Instant Mobile QR Code Export:** Render a high-contrast dark mode QR code in-app. Users open the official WireGuard iOS/Android app, scan the screen with their camera, and instantly sync their peer configuration in 2 seconds flat.

### 5.2 Zero-Trust 2FA (TOTP) Gated Tunnels
- **The Problem:** WireGuard protocol is connectionless and stateless UDP with zero native 2FA support.
- **Client-Side TOTP Gate:**
  - Standard RFC 6238 TOTP (compatible with Google Authenticator, 1Password, Bitwarden, Aegis).
  - Setup via in-app QR code seed generation in Settings.
  - Require a 6-digit TOTP code when roaming into an untrusted network, waking from sleep, or on an expired session timer (e.g. 8-hour shift).
  - Keeps the tunnel locked and disarmed until the physical 2FA device authorizes it.

### 5.3 Fail-Closed "Kill Switch With No Tunnel"
- **The Gap:** Standard VPN kill switches only engage *after* a tunnel is actively running, leaking background packets and DNS during connection phases or captive portals.
- **Pre-Tunnel Fortress:** If sitting on an untrusted network and no tunnel is active (or while awaiting TOTP verification), WireFox enforces an outbound drop-all rule via ephemeral WFP.
- **Strict Whitelist:**
  - DHCP (UDP 67/68) for local IP lease.
  - Local Gateway ARP/ICMP for identity and reachability checks.
  - Outbound UDP strictly destined to the WireGuard Endpoint IP + Port.
  - Absolutely zero unencrypted payload or DNS packets can leave the host into the clear.

### 5.4 Anti-Tamper & UI Bypass Prevention ("Preventing the Eye-Poke")
- **The Question:** *"What stops someone from walking up to the laptop, opening the official WireGuard GUI, and clicking 'Activate' behind WireFox's back?"*
- **Defense-in-Depth Measures:**
  1. **Kernel Endpoint Drop Filter:** WireFox's WFP filter blocks outgoing UDP packets to the WireGuard server endpoint IP until TOTP authentication succeeds. Even if the user or an attacker clicks "Activate" in the official GUI, the kernel drops handshake packets before they hit the NIC. WireGuard spins in retries without establishing a connection.
  2. **DPAPI Encrypted Key Injection:** Configurations store dummy keys on disk; the real Curve25519 private key is held in a DPAPI-encrypted vault and only injected into memory/service upon successful TOTP entry.
  3. **Headless Daemon Operation:** WireFox operates WireGuard tunnels directly as an SCM service manager, rendering the upstream tray app and GUI completely optional.


