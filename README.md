<p align="center">
  <img src="Assets/wirefox_icon.png" alt="WireFox Logo" width="128" height="128" />
</p>

<h1 align="center">WireFox</h1>

<p align="center">
  <em>Intelligent Network Roaming &amp; Watchdog Manager for WireGuard on Windows</em><br>
  <em>The WireGuard companion fox you didn't know you needed.</em><br>
  <em>Crafted with care by FoxDen Software</em>
</p>

<p align="center">
  <a href="https://github.com/TalviFox/WireFox"><img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue.svg" alt="Platform" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0%20(Self--Contained)-purple.svg" alt="Runtime" /></a>
  <a href="https://github.com/TalviFox/WireFox"><img src="https://img.shields.io/badge/Telemetry-0%20(100%25%20Private)-success.svg" alt="Zero Telemetry" /></a>
  <a href="https://www.wireguard.com/"><img src="https://img.shields.io/badge/Kernel%20Driver-Official%20WireGuardNT-success.svg" alt="Driver" /></a>
  <a href="https://github.com/microsoft/winget-pkgs/pull/433833"><img src="https://img.shields.io/badge/WinGet-FoxDenSoftware.WireFox-blue?logo=windows&logoColor=white" alt="WinGet Package" /></a>
  <a href="https://github.com/TalviFox/WireFox/releases"><img src="https://img.shields.io/github/downloads/TalviFox/WireFox/total?color=orange&label=Downloads" alt="GitHub Downloads" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-PolyForm%20Perimeter-blue.svg" alt="License" /></a>
</p>

> *"On iOS and Android, WireGuard connects when you leave the house and disconnects on your home Wi-Fi. On Windows laptops... you were left clicking buttons manually like a caveman or hacking fragile Task Scheduler scripts."*

---

## ⚡ TL;DR

On mobile devices, WireGuard has seamless on-demand rules: your phone automatically engages the VPN when you connect to public networks, and gracefully turns off when you're back on home Wi-Fi. On Windows, however, laptops have historically lacked intelligent roaming, leaving users with an annoying choice between manual toggling or sluggish speeds when local LAN traffic gets routed through remote tunnels.

**WireFox** bridges that gap. It is a lightweight, modern background assistant and system tray daemon for Windows that automates your WireGuard tunnel based on your current network environment. It validates trusted networks using hardware ARP gateway probing (`iphlpapi.dll`) to prevent evil-twin Wi-Fi spoofing, continuously monitors UDP handshake health, and provides instant tray controls.

<p align="center">
  <img src="docs/assets/screenshots/WireFox Main Screen.png" alt="WireFox Dashboard" width="850" />
</p>

---

## 🚀 Quick Install

### 🪟 Windows Package Manager (WinGet)

```powershell
winget install FoxDenSoftware.WireFox
```

### ⚡ One-Line Install (PowerShell)

Run in PowerShell as Administrator:

```powershell
irm https://raw.githubusercontent.com/TalviFox/WireFox/main/install.ps1 | iex
```

> [!NOTE]
> **Why Elevation is Required:** Managing Windows kernel tunnel services (`WireGuardTunnel$...`), routing table modifications, and raw ARP gateway queries requires Windows Administrator privileges. WireFox runs elevated silently at logon via a secure Windows Scheduled Task without prompting UAC on every boot.

Prefer a portable single-file executable? Grab `WireFox.exe` directly from the [Releases](https://github.com/TalviFox/WireFox/releases) page.

---

## ✨ Features at a Glance

🔄 **Intelligent Network Roaming**  
&nbsp;&nbsp;&nbsp;&nbsp;Automatically disconnects and bypasses the tunnel when connected to your home or office Wi-Fi for full gigabit LAN speeds; automatically re-engages WireGuard the second you connect to public Wi-Fi, hotel portals, or cellular hotspots.

🛡️ **Gateway MAC (ARP) Anti-Spoofing**  
&nbsp;&nbsp;&nbsp;&nbsp;Identifies trusted networks not just by Wi-Fi SSID name, but by default gateway MAC address using Layer-2 `SendARP`. Protects Ethernet connections and prevents "evil twin" rogue access points from tricking your PC into disabling VPN.

🐕 **Handshake & Session Watchdog**  
&nbsp;&nbsp;&nbsp;&nbsp;Actively monitors kernel handshake freshness via the official WireGuardNT command layer. Detects silent UDP drops, firewall blocks, captive portals, and driver sleep stalls, automatically executing graceful recovery before you notice a dropped connection.

🎨 **Dynamic 5-State Fox Shield**  
&nbsp;&nbsp;&nbsp;&nbsp;Glanceable, high-resolution visual feedback both in your system tray and on the main dashboard:
* 🟢 **Emerald Green:** Tunnel Active & Encrypted on untrusted networks.
* 🔷 **Classic Teal:** Connected to a Trusted Network (LAN / Wi-Fi) - Tunnel gracefully Bypassed.
* 🟠 **Amber Orange:** Temporary Session Active (15m Pause, Split-Tunnel, or Connecting).
* 🔴 **Crimson Red:** Unprotected Alert - Disconnected on an untrusted public network.
* ⚪ **Monochrome Gray:** Inactive / Manually Disabled / Offline (Airplane Mode).

⚡ **Context-Aware Quick Actions**  
&nbsp;&nbsp;&nbsp;&nbsp;Smart status controls that adapt to your connection state:
* **Dynamic Pause / Resume Toggle:** Pause protection for 15 minutes when needed; while paused, the button transforms into **Resume VPN** ▶️ so you can cancel early with a single click.
* **Smart Split-Tunneling:** Temporarily routes only specific subnets through the tunnel; grays out with clear tooltips if not configured.
* **Contextual Full Tunnel / Restart:** Restore full routing when split-tunneling, or perform a clean **Restart Tunnel** 🔄 connection drop and key renegotiation for instant troubleshooting.

💻 **Native Windows 10 & 11 Experience**  
&nbsp;&nbsp;&nbsp;&nbsp;Crafted with modern WPF and native dark/light themes. Sits quietly in the system tray, responds to interactive Windows Toast notifications with actionable buttons, and consumes zero CPU while idle.

🔒 **Zero Telemetry & 100% Audited Driver**  
&nbsp;&nbsp;&nbsp;&nbsp;No accounts, no external cloud dependencies, no background telemetry, and zero ads. Interacts directly with the official, audited **WireGuardNT** service driver.

---

## 🔒 Binary Verification & Security Audit

WireFox is open-source and independent. You can cryptographically verify the integrity of your downloaded executable directly against GitHub Releases at any time without trusting third-party certificates:

```powershell
irm https://raw.githubusercontent.com/TalviFox/WireFox/main/verify.ps1 | iex
```

---

## 🧹 Clean System Removal

WireFox respects your system. It leaves no lingering drivers, registry residue, or background daemons. You can uninstall natively from **Windows Settings > Installed Apps**, via **Settings > Clean System Removal** in the app, or with one command:

```powershell
irm https://raw.githubusercontent.com/TalviFox/WireFox/main/uninstall.ps1 | iex
```

---

## 🛠️ Building from Source

WireFox is built using the **.NET 10.0 Windows Desktop SDK**.

```powershell
# Clone the repository
git clone https://github.com/TalviFox/WireFox.git
cd WireFox

# Build single-file release executable
dotnet publish WireFox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

Or run `build.bat` in the root directory.

---

## 🤖 Transparency & AI Disclosure

WireFox is developed with the assistance of AI coding tools. In the spirit of open development and personal accountability: **I don't post what I don't run.**

Every feature, script, and build is actively dogfooded, tested, and run on my own daily-driver machines before it is published here.

---

## ⚖️ License

WireFox source code is available under the **[PolyForm Perimeter License 1.0.0](LICENSE)**.

### What this means:
* ✅ **Open to inspect, fork, and hack:** You are free to run, study, modify, build, and distribute WireFox for personal, educational, or internal use.
* 🛡️ **Anti-theft & non-compete protection:** You may not use this software or its source code to provide or market any product or service that competes with WireFox or acts as a commercial substitute for it.

For full license terms, see the [LICENSE](LICENSE) file or visit [PolyForm Project](https://polyformproject.org/licenses/perimeter/1.0.0).

---

## 🏷️ Trademarks & Attribution

* **[WireGuard®](https://www.wireguard.com)** is a registered trademark of **Jason A. Donenfeld**.
* **WireFox** is an independent companion project developed by **FoxDen Software**. It is not affiliated with, endorsed by, or sponsored by Jason A. Donenfeld or the official WireGuard development team.

---

<p align="center">
  <a href="https://github.com/TalviFox">
    <img src="docs/assets/FoxDen_Minimal_Universal.png" alt="FoxDen Software" width="220" />
  </a><br>
  <sub><em>Crafted with care by <strong>FoxDen Software</strong></em></sub>
</p>
