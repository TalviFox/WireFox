# WireFox Automated Release Script
# https://github.com/TalviFox/WireFox
# Usage: .\release.ps1 -Version "1.1.0"

[CmdletBinding()]
param(
    [Parameter(Position=0, Mandatory=$false)]
    [string]$Version,

    [Parameter()]
    [switch]$DryRun,

    [Parameter()]
    [switch]$SkipGit
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "Stop"

Write-Host @"
  =============================================================
     🦊 WireFox Release Builder & Hash Signer
     FoxDen Software
  =============================================================
"@ -ForegroundColor DarkCyan

$root = $PSScriptRoot
$csprojPath = Join-Path $root "WireFox.csproj"
$publishDir = Join-Path $root "publish"

# 1. Determine Target Version
[xml]$projXml = Get-Content $csprojPath
$currentVersionNode = $projXml.Project.PropertyGroup.Version
$currentVersion = if ($currentVersionNode) { $currentVersionNode.Trim() } else { "1.0.0" }

if (-not $Version) {
    Write-Host "[?] Current version in WireFox.csproj is: v$currentVersion" -ForegroundColor Yellow
    $suggested = [Version]::Parse($currentVersion)
    $nextPatch = "$($suggested.Major).$($suggested.Minor).$($suggested.Build + 1)"
    $inputVer = Read-Host "    Enter release version to build (default: $nextPatch)"
    $Version = if ([string]::IsNullOrWhiteSpace($inputVer)) { $nextPatch } else { $inputVer.Trim() }
}

$cleanVersion = $Version.Trim().TrimStart('v', 'V')
Write-Host "[*] Target Release Version: v$cleanVersion" -ForegroundColor Cyan

# 2. Update WireFox.csproj Version
if (-not $DryRun) {
    Write-Host "[*] Updating <Version> in WireFox.csproj..." -ForegroundColor Cyan
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = [regex]::Replace($csprojContent, "<Version>.*?</Version>", "<Version>$cleanVersion</Version>")
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding UTF8
}

# 3. Locate .NET SDK
$dotnetPath = "dotnet"
$localDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (Test-Path $localDotnet) {
    $dotnetPath = $localDotnet
}

Write-Host "[*] Using .NET toolchain: $dotnetPath" -ForegroundColor Cyan

# 4. Clean and Publish Single-File Release
Write-Host "[*] Publishing single-file release executable (win-x64)..." -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}

$publishArgs = @(
    "publish",
    $csprojPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-o", $publishDir
)

& $dotnetPath $publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[X] Build failed with exit code $LASTEXITCODE!" -ForegroundColor Red
    return
}

$targetExe = Join-Path $publishDir "WireFox.exe"
if (-not (Test-Path $targetExe)) {
    Write-Host "`n[X] Published executable was not found at $targetExe!" -ForegroundColor Red
    return
}

# 5. Calculate SHA-256 Hash and generate SHA256SUMS.txt
Write-Host "[*] Calculating cryptographic SHA-256 hash..." -ForegroundColor Cyan
$hash = (Get-FileHash -Path $targetExe -Algorithm SHA256).Hash.ToLowerInvariant()
$fileSizeMb = [math]::Round(((Get-Item $targetExe).Length / 1MB), 2)

$checksumsFile = Join-Path $publishDir "SHA256SUMS.txt"
$checksumEntry = "$hash  WireFox.exe"
Set-Content -Path $checksumsFile -Value $checksumEntry -Encoding ASCII

# Also write individual hash file for convenience
$hashFile = Join-Path $publishDir "WireFox.exe.sha256"
Set-Content -Path $hashFile -Value $hash -Encoding ASCII

Write-Host "[+] Binary SHA-256: $hash" -ForegroundColor Green
Write-Host "[+] Checksums file: $checksumsFile" -ForegroundColor Green

# 6. Git Commit & Tagging (if not DryRun or SkipGit)
if (-not $DryRun -and -not $SkipGit) {
    try {
        Write-Host "[*] Creating Git release commit and tag v$cleanVersion..." -ForegroundColor Cyan
        git add WireFox.csproj
        git commit -m "Release v$cleanVersion" -ErrorAction SilentlyContinue
        git tag -a "v$cleanVersion" -m "WireFox Release v$cleanVersion"
        Write-Host "[+] Git tag 'v$cleanVersion' created." -ForegroundColor Green
    } catch {
        Write-Host "[!] Git tagging failed: $_" -ForegroundColor Yellow
    }
}

# 7. Output Release Guidance
$bt = [char]96 # Backtick character
$tripleBt = "$bt$bt$bt"

Write-Host @"

  =============================================================
     🎉 RELEASE v$cleanVersion BUILT SUCCESSFULLY!
  =============================================================
  Output Binary:    $targetExe ($fileSizeMb MB)
  SHA-256 Hash:     $hash
  Checksums File:   $checksumsFile

  -------------------------------------------------------------
  📋 COPY & PASTE FOR GITHUB RELEASE NOTES:
  -------------------------------------------------------------
  ## 🔒 Checksums & Binary Verification
  | File | SHA-256 Checksum |
  | :--- | :--- |
  | **WireFox.exe** | ${bt}$hash${bt} |

  Verify before running (PowerShell):
  ${tripleBt}powershell
  irm https://raw.githubusercontent.com/TalviFox/WireFox/main/verify.ps1 | iex
  ${tripleBt}
  -------------------------------------------------------------

  📦 GITHUB RELEASE ASSETS TO UPLOAD:
  1. publish\WireFox.exe
  2. publish\SHA256SUMS.txt
  3. publish\release_notes.md (Use this for the GitHub release body!)

  🚀 NEXT STEP (GIT PUSH):
  git push origin HEAD --tags
  =============================================================
"@ -ForegroundColor Green

# 8. Generate release_notes.md template
$releaseNotesPath = Join-Path $publishDir "release_notes.md"
$releaseNotesTemplate = @"
# 🦊 WireFox v$cleanVersion

WireFox bridges the missing link of WireGuard on Windows: intelligent background roaming and kernel-level tunnel watchdog protection.

## 📝 What's New in v$cleanVersion
- Add a bullet point here about what changed!

## ✨ Highlights
- 🔄 **Auto-Roaming:** Bypasses VPN on trusted home/office Wi-Fi for full gigabit LAN speeds; automatically connects on untrusted networks.
- 🛡️ **Gateway ARP Anti-Spoofing:** Identifies trusted networks via default gateway MAC addresses (iphlpapi.dll) to prevent SSID spoofing.
- 🐕 **Handshake Watchdog:** Actively polls kernel timestamps (wg.exe) to detect silent UDP drops, captive portals, and dead tunnels.
- 💻 **Native Windows Experience:** Built with modern Fluent/Mica design, Windows System Tray integration, and actionable Toast notifications.
- 🔒 **Zero Bloat, Zero Telemetry:** No user tracking, no accounts, and direct interaction with the audited WireGuardNT driver.

## ⚡ Quick Install / Upgrade (PowerShell)

Run PowerShell as Administrator to install or seamlessly upgrade in place:

${tripleBt}powershell
irm https://raw.githubusercontent.com/TalviFox/WireFox/main/install.ps1 | iex
${tripleBt}

## 🔒 Checksums & Binary Verification
| File | SHA-256 Checksum |
| :--- | :--- |
| **WireFox.exe** | `${bt}$hash${bt}` |

Verify integrity before running (PowerShell):
${tripleBt}powershell
irm https://raw.githubusercontent.com/TalviFox/WireFox/main/verify.ps1 | iex
${tripleBt}
"@

Set-Content -Path $releaseNotesPath -Value $releaseNotesTemplate -Encoding UTF8
Write-Host "[+] Draft release notes saved to $releaseNotesPath" -ForegroundColor Green
