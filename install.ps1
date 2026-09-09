# WireFox Installer Script
# https://github.com/TalviFox/WireFox
# Automated Roaming & Watchdog Manager for WireGuard on Windows

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "Stop"

Write-Host @"
  ======================================================
     🦊 WireFox Installer
     WireGuard Roaming & Watchdog Manager for Windows
  ======================================================
"@ -ForegroundColor DarkCyan

# 1. Check for Administrator privileges (required for WireGuard NT service control)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "`n[!] Administrator privileges are required to install WireFox and control WireGuard services." -ForegroundColor Yellow
    Write-Host "[*] Requesting elevation..." -ForegroundColor Cyan
    $installerUrl = "https://raw.githubusercontent.com/TalviFox/WireFox/main/install.ps1"
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -Command `"irm $installerUrl | iex`""
    return
}

# 2. Check for WireGuard installation
$wireguardExe = "$env:ProgramFiles\WireGuard\wireguard.exe"
if (-not (Test-Path $wireguardExe)) {
    Write-Host "`n[!] WireGuard for Windows was not detected at standard location ($wireguardExe)." -ForegroundColor Yellow
    Write-Host "    WireFox requires WireGuard to manage tunnels." -ForegroundColor Yellow
    Write-Host "    Download it from: https://www.wireguard.com/install/`n" -ForegroundColor DarkYellow
}

# 3. Setup install directory & temp staging
$installDir = "$env:ProgramFiles\WireFox"
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

$tempDir = Join-Path $env:TEMP "WireFox_Installer"
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}

$stagingExe = Join-Path $tempDir "WireFox.exe"
$targetExe = Join-Path $installDir "WireFox.exe"

# 4. Check for Local Source Code and Build, or Download Release
$repo = "TalviFox/WireFox"
$expectedHash = $null
$releaseTag = "v1.0.0"

$localProj = Join-Path $PSScriptRoot "WireFox.csproj"
if (Test-Path $localProj) {
    Write-Host "[*] Local source code detected. Building locally instead of downloading..." -ForegroundColor Cyan
    $publishDir = Join-Path $PSScriptRoot "publish"
    
    $dotnet = "dotnet"
    $dotnetDir = "$env:LOCALAPPDATA\Microsoft\dotnet"
    if (Test-Path "$dotnetDir\dotnet.exe") {
        $dotnet = "$dotnetDir\dotnet.exe"
    }
    
    $proc = Start-Process $dotnet -ArgumentList "publish `"$localProj`" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o `"$publishDir`"" -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) {
        Write-Host "`n[X] Local build failed with exit code $($proc.ExitCode)." -ForegroundColor Red
        return
    }
    
    $builtExe = Join-Path $publishDir "WireFox.exe"
    if (-not (Test-Path $builtExe)) {
        Write-Host "`n[X] Local build succeeded but WireFox.exe not found at $builtExe." -ForegroundColor Red
        return
    }
    
    Copy-Item -Path $builtExe -Destination $stagingExe -Force
    Write-Host "[+] Local build compiled and verified." -ForegroundColor Green
} else {
    $apiUrl = "https://api.github.com/repos/$repo/releases/latest"
    $headers = @{ "User-Agent" = "WireFox-Installer" }

    Write-Host "[*] Querying latest release from $repo over TLS..." -ForegroundColor Cyan

    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing
        $releaseTag = $release.tag_name

        # Try to find checksums in assets
        $sumsAsset = $release.assets | Where-Object { $_.name -match "^(SHA256SUMS|checksums|WireFox.*)\.(txt|sha256)$" -or $_.name -eq "WireFox.exe.sha256" } | Select-Object -First 1
        if ($sumsAsset) {
            $checksumText = Invoke-RestMethod -Uri $sumsAsset.browser_download_url -Headers $headers -UseBasicParsing
            if ($checksumText -match "([a-fA-F0-9]{64})\s+.*WireFox\.exe") {
                $expectedHash = $matches[1].ToLowerInvariant()
            } elseif ($checksumText -match "\b([a-fA-F0-9]{64})\b") {
                $expectedHash = $matches[1].ToLowerInvariant()
            }
        }

        # Fallback to body regex if not in assets
        if (-not $expectedHash -and $release.body -match "\b([a-fA-F0-9]{64})\b") {
            $expectedHash = $matches[1].ToLowerInvariant()
        }
    } catch {
        Write-Host "[!] Could not query release API metadata. Proceeding with direct binary download..." -ForegroundColor Yellow
    }

    $downloadUrl = "https://github.com/$repo/releases/latest/download/WireFox.exe"
    Write-Host "[*] Downloading WireFox ($releaseTag)..." -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $stagingExe -UseBasicParsing
    } catch {
        Write-Host "`n[X] Failed to download WireFox.exe from GitHub Releases ($downloadUrl)." -ForegroundColor Red
        Write-Host "    Please ensure a Release containing 'WireFox.exe' exists at:" -ForegroundColor Yellow
        Write-Host "    https://github.com/$repo/releases" -ForegroundColor White
        return
    }

    # 5. Enforce SHA-256 Integrity Check
    Write-Host "[*] Verifying cryptographic SHA-256 integrity..." -ForegroundColor Cyan
    $actualHash = (Get-FileHash -Path $stagingExe -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "    Downloaded SHA-256: $actualHash" -ForegroundColor White

    if ($expectedHash) {
        if ($actualHash -ne $expectedHash) {
            Write-Host "`n[X] CRITICAL: SHA-256 CHECKSUM VERIFICATION FAILED!" -ForegroundColor Red
            Write-Host "    Expected: $expectedHash" -ForegroundColor Yellow
            Write-Host "    Actual:   $actualHash" -ForegroundColor Yellow
            Write-Host "    The downloaded binary has been discarded to protect your system." -ForegroundColor Red
            Remove-Item -Path $stagingExe -Force -ErrorAction SilentlyContinue
            return
        }
        Write-Host "[+] SHA-256 integrity verified successfully! (100% Match with GitHub Release)" -ForegroundColor Green
    } else {
        Write-Host "[!] Notice: No published checksum found in release metadata. Proceeding with caution." -ForegroundColor Yellow
    }
}

# 6. Stop running instance if updating
$running = Get-Process -Name "WireFox" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[*] Stopping running WireFox process..." -ForegroundColor Cyan
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# 7. Move verified binary into place
Copy-Item -Path $stagingExe -Destination $targetExe -Force
Remove-Item -Path $stagingExe -Force -ErrorAction SilentlyContinue
Write-Host "[+] Installed to: $targetExe" -ForegroundColor Green

# 8. Deploy uninstaller script
$uninstallerUrl = "https://raw.githubusercontent.com/$repo/main/uninstall.ps1"
$uninstallerTarget = Join-Path $installDir "uninstall.ps1"
try {
    Invoke-WebRequest -Uri $uninstallerUrl -OutFile $uninstallerTarget -UseBasicParsing
    Write-Host "[+] Uninstaller script ready: $uninstallerTarget" -ForegroundColor Green
} catch {
    # If offline / local development, try to copy local uninstall.ps1
    $localUninstall = Join-Path $PSScriptRoot "uninstall.ps1"
    if (Test-Path $localUninstall) {
        Copy-Item -Path $localUninstall -Destination $uninstallerTarget -Force
    }
}

# 9. Register in Windows "Installed Apps" (Add or Remove Programs)
try {
    $regKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WireFox"
    if (-not (Test-Path $regKey)) {
        New-Item -Path $regKey -Force | Out-Null
    }
    Set-ItemProperty -Path $regKey -Name "DisplayName" -Value "WireFox"
    Set-ItemProperty -Path $regKey -Name "DisplayVersion" -Value $releaseTag.TrimStart('v','V')
    Set-ItemProperty -Path $regKey -Name "Publisher" -Value "FoxDen Software"
    Set-ItemProperty -Path $regKey -Name "DisplayIcon" -Value "$targetExe,0"
    Set-ItemProperty -Path $regKey -Name "InstallLocation" -Value $installDir
    Set-ItemProperty -Path $regKey -Name "UninstallString" -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallerTarget`""
    Set-ItemProperty -Path $regKey -Name "URLInfoAbout" -Value "https://github.com/TalviFox/WireFox"
    Write-Host "[+] Registered in Windows Installed Apps (Add or Remove Programs)." -ForegroundColor Green
} catch {
    Write-Host "[!] Could not register in Windows Uninstall registry: $_" -ForegroundColor Yellow
}

# 10. Create Start Menu Shortcut
try {
    $wsh = New-Object -ComObject WScript.Shell
    $startMenuDir = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs"
    $shortcut = $wsh.CreateShortcut((Join-Path $startMenuDir "WireFox.lnk"))
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$targetExe,0"
    $shortcut.Description = "Automated Roaming & Watchdog Manager for WireGuard on Windows"
    $shortcut.Save()
    Write-Host "[+] Start Menu shortcut created." -ForegroundColor Green
} catch {
    Write-Host "[!] Could not create Start Menu shortcut: $_" -ForegroundColor Yellow
}

# 11. Complete & Launch
Write-Host @"

  ======================================================
     ✅ WireFox has been installed successfully!
  ======================================================
"@ -ForegroundColor Green

Write-Host "[*] Launching WireFox..." -ForegroundColor Cyan
Start-Process $targetExe
