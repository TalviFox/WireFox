# WireFox Uninstaller Script
# https://github.com/TalviFox/WireFox
# Cleanly removes WireFox background tasks, shortcuts, registry entries, and program files.

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ErrorActionPreference = "SilentlyContinue"

Write-Host @"
  =============================================================
     🦊 WireFox Uninstaller
     Clean & Complete System Removal
  =============================================================
"@ -ForegroundColor DarkYellow

# 1. Administrator Elevation Check
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "`n[!] Administrator privileges are required to clean up scheduled tasks and Program Files." -ForegroundColor Yellow
    Write-Host "[*] Requesting elevation..." -ForegroundColor Cyan
    if ($PSCommandPath -and (Test-Path $PSCommandPath)) {
        Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    } else {
        $tempScript = Join-Path $env:TEMP "WireFox_uninstall.ps1"
        $uninstallerUrl = "https://raw.githubusercontent.com/TalviFox/WireFox/main/uninstall.ps1"
        Invoke-WebRequest -Uri $uninstallerUrl -OutFile $tempScript -UseBasicParsing
        Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$tempScript`""
    }
    return
}

# 2. Stop running WireFox process
Write-Host "[*] Terminating running WireFox processes..." -ForegroundColor Cyan
Get-Process -Name "WireFox" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "WGManager" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

# 3. Remove elevated Task Scheduler jobs
Write-Host "[*] Removing Task Scheduler background tasks..." -ForegroundColor Cyan
Unregister-ScheduledTask -TaskName "WireFox" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
Unregister-ScheduledTask -TaskName "WireGuardManager" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
schtasks.exe /Delete /TN "WireFox" /F *>$null
schtasks.exe /Delete /TN "WireGuardManager" /F *>$null

# 4. Remove Start Menu shortcuts
Write-Host "[*] Removing Start Menu shortcuts..." -ForegroundColor Cyan
$startMenuShortcut = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\WireFox.lnk"
if (Test-Path $startMenuShortcut) {
    Remove-Item -Path $startMenuShortcut -Force -ErrorAction SilentlyContinue
}
$legacyShortcut = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\WireGuardManager.lnk"
if (Test-Path $legacyShortcut) {
    Remove-Item -Path $legacyShortcut -Force -ErrorAction SilentlyContinue
}

# 5. Remove registry entries (Uninstall & Run keys)
Write-Host "[*] Cleaning registry entries..." -ForegroundColor Cyan
Remove-Item -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WireFox" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "WireFox" -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "WireFox" -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "WireGuardManager" -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "WireGuardManager" -Force -ErrorAction SilentlyContinue

# 6. Prompt user regarding saved configuration data
Write-Host ""
$localAppData = "$env:LocalAppData\WireFox"
$roamingAppData = "$env:AppData\WireFox"
$hasData = (Test-Path $localAppData) -or (Test-Path $roamingAppData)

if ($hasData) {
    Write-Host "[?] Would you also like to delete your saved configurations, trusted networks, and logs in %AppData%?" -ForegroundColor Yellow
    $response = Read-Host "    Type 'y' to delete user data, or press Enter to keep it safe (y/N)"
    if ($response -match "^[yY]") {
        Remove-Item -Path $localAppData -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $roamingAppData -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "[+] Removed configuration files from AppData." -ForegroundColor Green
    } else {
        Write-Host "[*] Configuration files preserved in AppData (safe for reinstall)." -ForegroundColor Cyan
    }
}

# 7. Remove Program Files directory
$installDir = "$env:ProgramFiles\WireFox"
if (Test-Path $installDir) {
    Write-Host "[*] Removing program files at $installDir..." -ForegroundColor Cyan
    Get-ChildItem -Path $installDir -Exclude "uninstall.ps1" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    
    # Detach a quick background command to remove the folder once this script exits
    Start-Process cmd.exe -ArgumentList "/c timeout /t 1 /nobreak >nul & rmdir /s /q `"$installDir`"" -WindowStyle Hidden
}

Write-Host @"

  =============================================================
     ✅ WireFox has been cleanly and completely uninstalled.
     Thank you for using WireFox!
  =============================================================
"@ -ForegroundColor Green
Start-Sleep -Seconds 2
