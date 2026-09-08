# 🦊 WireFox

<div align="center">
  <img src="wirefox_icon.png" width="160" alt="WireFox Logo" />
  <br />
  <strong>Automated Roaming & Watchdog Manager for WireGuard on Windows</strong>
  <br />
  <em>Crafted with care by FoxDen Software</em>
  <p>
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-PolyForm_Perimeter_1.0.0-blue.svg" alt="License: PolyForm Perimeter 1.0.0" /></a>
    <img src="https://img.shields.io/badge/.NET-8.0-purple.svg" alt=".NET 8.0" />
    <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg" alt="Windows 10 / 11" />
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

---

## 🛠️ Building from Source

WireFox is built on **.NET 8.0 Windows Desktop SDK**.

```powershell
# Clone the repository
git clone https://github.com/TalviFox/WireFox.git
cd WireFox

# Build single-file release executable
dotnet publish WireFox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

Or run `build.bat` in the root directory.

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
