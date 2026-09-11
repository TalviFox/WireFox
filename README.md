# WireFox

<div align="center">
  <img src="wirefox_icon.png" width="160" alt="WireFox Logo" />
  <br />
  <strong>Automated Roaming & Watchdog Manager for WireGuard on Windows</strong>
  <br />
  <em>The WireGuard companion fox you didn't think you'd need.</em>
  <br />
  <em>Crafted with care by FoxDen Software</em>
  <p>
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-PolyForm_Perimeter_1.0.0-blue.svg" alt="License: PolyForm Perimeter 1.0.0" /></a>
    <img src="https://img.shields.io/badge/.NET-10.0-purple.svg" alt=".NET 10.0" />
    <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg" alt="Windows 10 / 11" />
    <a href="https://github.com/TalviFox/WireFox/releases"><img src="https://img.shields.io/github/downloads/TalviFox/WireFox/total?color=orange&label=Downloads" alt="GitHub Downloads" /></a>
  </p>
</div>

---

## ⚡ The Missing Piece of WireGuard on Windows

On mobile devices (iOS / Android), WireGuard has native on-demand rules: your phone automatically connects to VPN when you leave your house, and turns off when you're connected to home Wi-Fi. 

On Windows, however, laptops have historically lacked intelligent roaming. Users were left with only two choices: manually clicking connect every time they leave the house, or hacking together fragile Task Scheduler scripts.

**WireFox** bridges that gap. It is a lightweight, modern background assistant and system tray daemon for Windows that automates your WireGuard tunnel based on your current network environment.

---

## ✨ Features

- 🔄 **Intelligent Network Roaming**
  - **Auto-Bypass on Trusted Wi-Fi:** Disconnects VPN when connected to your home or office Wi-Fi for full gigabit LAN speeds.
  - **Auto-Connect on Untrusted Networks:** Automatically spins up the WireGuard tunnel the moment you connect to an open or untrusted network (coffee shops, hotels, airports).
- 🛡️ **Gateway MAC (ARP) Anti-Spoofing**
  - Identifies trusted networks not just by SSID name, but by default gateway MAC address (`iphlpapi.dll` SendARP).
  - Protects Ethernet connections and guards against rogue Wi-Fi access points spoofing your home SSID.
- 🐕 **Handshake & Session Watchdog**
  - Actively polls kernel handshake timestamps via the official `wg.exe` command layer.
  - Detects silent UDP drops, captive portals, and dead tunnels.
  - Built-in timer controls: pause protection for 15 minutes or temporarily switch to split-tunnel mode.
- 💻 **Native Windows Experience**
  - Built with modern **WPF** and **Mica/Fluent Design** (dark/light themes).
  - Sits quietly in the Windows System Tray with quick controls.
  - Native Windows Toast notifications with actionable inline buttons.
  - Starts silently on Windows boot via elevated Scheduled Task without annoying UAC prompts.
- 🔒 **Zero Bloat, Zero Telemetry**
  - No accounts, no background analytics, no ads.
  - Interacts directly with the official, audited **WireGuardNT** service driver.

---

## 🚀 Quick Start & Installation

### Requirements
- **Windows 10 (version 19041+) or Windows 11**
- **Official WireGuard for Windows** installed ([wireguard.com/install](https://www.wireguard.com/install/))

### ⚡ One-Line Install (PowerShell)

Run the following in PowerShell (Administrator recommended):

```powershell
irm https://raw.githubusercontent.com/TalviFox/WireFox/main/install.ps1 | iex
```

> This automatically fetches the latest release, installs WireFox to `Program Files`, and adds a Start Menu shortcut.

### 📦 Manual Download
1. Download the latest `WireFox.exe` from [Releases](https://github.com/TalviFox/WireFox/releases/latest).
2. Launch `WireFox.exe` as Administrator (required to manage Windows tunnel services).
3. Select your tunnel in **Settings** (or import a `.conf` file).
4. Add your home Wi-Fi or router to **Trusted Networks**.

### 🔒 Verify Binary Integrity (SHA-256)
WireFox is open-source and independent. You can audit the integrity of your binary directly against GitHub Releases at any time without commercial code-signing:
```powershell
irm https://raw.githubusercontent.com/TalviFox/WireFox/main/verify.ps1 | iex
```

### 🧹 Clean Uninstall
WireFox can be removed natively from Windows Settings > **Installed Apps**, directly inside WireFox under **Settings > Clean System Removal**, or via PowerShell:
```powershell
irm https://raw.githubusercontent.com/TalviFox/WireFox/main/uninstall.ps1 | iex
```

---

## 📝 Changelog

### v1.0.5
- **Fix:** Replaced native `wireguard.exe` tunnel uninstallation with silent `sc.exe delete` to eliminate "Service does not exist" Error UI popups when disconnecting/trusting networks.
- **Fix:** Overhauled gateway storage logic to uniquely identify trusted networks by IP + MAC Address, resolving a major bug where multiple networks sharing the same default gateway (e.g. 192.168.1.1) would overwrite each other and trigger false-positive anti-spoofing lockouts.


### v1.0.4
- **Resilient Tunnel Deactivation & Zombie Prevention:** Resolved the WireGuard deactivation freeze where driver deadlocks left services spinning in `StopPending` state. Added graceful stop timeout (3s) with immediate escalation to hosting process termination, SCM driver unbinding, and active DNS cache resolver flushing (`DnsFlushResolverCache`) to prevent Windows Filtering Platform (WFP) kernel blackholes.
- **Interactive Tray Control:** Added direct **Kill WireGuard (Force Stop)** action item to the system tray.
- **Differential Watchdog Diagnostics:** Integrated Layer-2 default gateway ARP probing (`ArpService`) and ICMP verification to validate physical network reachability before attributing connection loss to WireGuard, eliminating false-positive restarts when physical connectivity is lost.
- **Active End-to-End Connectivity Probing:** Handshake watchdog actively tests HTTP 204 endpoints (`generate_204`) with fallback DNS resolution checks against `msftconnecttest.com` to detect silent UDP drops and dead routes.
- **Intelligent Toast Notification Debouncing:** Replaced rapid notification spam during network transitions with in-place Windows toast replacements (tagged `wirefox` group) and strict cooldown timers (10m for network discovery, 3m for watchdog alerts).
- **Encoding & Script Hardening:** Fixed a lot of the PowerShell emoji mojibake across `install.ps1`, `uninstall.ps1`, `verify.ps1`, and `release.ps1` by moving to runtime surrogate generation, XML entity escaping, and enforcing UTF-8 without BOM across all build pipelines.
- **Re-tooled Installer Logic:** Updated install options and double click behavior as well as script theming.

### v1.0.3
- **Security Hardening & Least-Privilege Guardrails:**
  - **Fail-Closed In-Place Updates:** Enforced strict cryptographic SHA-256 integrity verification before executing updates, blocking untrusted or unverified binaries.
  - **Hardened Update Staging:** In-place updates now stage exclusively inside protected `Program Files\WireFox\Updates` instead of user-writable `%TEMP%` to eliminate local privilege escalation risks.
  - **Safe Interface Discovery:** Tunnel `.conf` discovery now enforces strict interface name validation and structural checks to prevent arbitrary file reading.
  - **Portable Mode Guardrails:** Running portably outside `Program Files` now alerts the user, blocks registering elevated scheduled tasks from untrusted directories, and provides a 1-click install to `Program Files`.
  - **Installed Apps Version Sync:** Automatically synchronizes the registered Windows DisplayVersion upon launch to eliminate version drift in Windows Settings.

### v1.0.2
- **Fixed Zombie Tunnels:** Fixed an issue where a tunnel could be left indefinitely active if the underlying Windows service wasn't properly registered with the Service Control Manager. Now gracefully falls back to explicit interface uninstallation.

---

## 🛠️ Building from Source

WireFox is built on **.NET 10.0 Windows Desktop SDK**.

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
- ✅ **Open to inspect, fork, and hack:** You are free to run, study, modify, build, and distribute WireFox for personal, educational, or internal use.
- 🛡️ **Anti-theft & non-compete protection:** You may not use this software or its source code to provide or market any product or service that competes with WireFox or acts as a commercial substitute for it.

For full license terms, see the [LICENSE](LICENSE) file or visit [PolyForm Project](https://polyformproject.org/licenses/perimeter/1.0.0).

---

## 🏷️ Trademarks & Attribution

- **WireGuard®** is a registered trademark of **Jason A. Donenfeld**.
- **WireFox** is an independent companion project developed by **FoxDen Software**. It is not affiliated with, endorsed by, or sponsored by Jason A. Donenfeld or the official WireGuard development team.
