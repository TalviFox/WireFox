# WireFox SHA-256 Integrity Auditor
# https://github.com/TalviFox/WireFox
# This script runs independently of WireFox.exe to audit its binary integrity directly against GitHub.

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$foxEmoji = [char]::ConvertFromUtf32(0x1F98A)
$bullet = [char]0x2022

Write-Host @"
  =============================================================
     $foxEmoji WireFox Binary Integrity Auditor
     Independent External Audit Anchor (GitHub TLS)
  =============================================================
"@ -ForegroundColor DarkCyan

# 1. Locate the target binary
$targetExe = $null

# Priority 1: Currently running WireFox process
$runningProc = Get-Process -Name "WireFox" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($runningProc) {
    try {
        $targetExe = $runningProc.MainModule.FileName
    }
    catch {
        $targetExe = (Get-Process -Id $runningProc.Id).Path
    }
}

# Priority 2: Standard installation in Program Files
if (-not $targetExe -or -not (Test-Path $targetExe)) {
    $standardPath = "$env:ProgramFiles\WireFox\WireFox.exe"
    if (Test-Path $standardPath) {
        $targetExe = $standardPath
    }
}

# Priority 3: Current directory / script directory
if (-not $targetExe -or -not (Test-Path $targetExe)) {
    $localPath = Join-Path $PSScriptRoot "WireFox.exe"
    $publishPath = Join-Path $PSScriptRoot "publish\WireFox.exe"
    if (Test-Path $localPath) {
        $targetExe = $localPath
    }
    elseif (Test-Path $publishPath) {
        $targetExe = $publishPath
    }
}

if (-not $targetExe -or -not (Test-Path $targetExe)) {
    Write-Host "[X] WireFox.exe was not found running or installed at $env:ProgramFiles\WireFox\WireFox.exe." -ForegroundColor Red
    Write-Host "    If installed in a custom location, please navigate to that directory and run verify.ps1." -ForegroundColor Yellow
    return
}

# 2. Compute local file hash and metadata
Write-Host "[*] Auditing target binary: $targetExe" -ForegroundColor Cyan
$fileItem = Get-Item $targetExe
$localVersion = $fileItem.VersionInfo.ProductVersion
if (-not $localVersion) { $localVersion = $fileItem.VersionInfo.FileVersion }
if (-not $localVersion) { $localVersion = "1.0.0" }
$localVersion = $localVersion.Trim()

$localHash = (Get-FileHash -Path $targetExe -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "    Version Reported: $localVersion" -ForegroundColor White
Write-Host "    Local SHA-256:    $localHash" -ForegroundColor White
Write-Host ""

# 3. Query official releases on GitHub
$repo = "TalviFox/WireFox"
$apiUrl = "https://api.github.com/repos/$repo/releases"

Write-Host "[*] Querying official releases from GitHub over TLS ($apiUrl)..." -ForegroundColor Cyan

try {
    $headers = @{ "User-Agent" = "WireFox-Integrity-Auditor" }
    $releases = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing
}
catch {
    Write-Host "[!] Could not query GitHub Releases API: $_" -ForegroundColor Yellow
    Write-Host "    Please ensure you have an active internet connection." -ForegroundColor DarkYellow
    return
}

if (-not $releases -or $releases.Count -eq 0) {
    Write-Host "[!] No published GitHub releases found for repository $repo." -ForegroundColor Yellow
    return
}

$latestRelease = $releases[0]
$matchedRelease = $null

function Get-ReleaseHash($release) {
    # Check assets for SHA256SUMS.txt, checksums.txt, or WireFox.exe.sha256
    $sumsAsset = $release.assets | Where-Object { $_.name -match "^(SHA256SUMS|checksums|WireFox.*)\.(txt|sha256)$" -or $_.name -eq "WireFox.exe.sha256" } | Select-Object -First 1
    if ($sumsAsset) {
        try {
            $content = Invoke-RestMethod -Uri $sumsAsset.browser_download_url -Headers $headers -UseBasicParsing
            if ($content -match "([a-fA-F0-9]{64})\s+.*WireFox\.exe") {
                return $matches[1].ToLowerInvariant()
            }
            if ($content -match "\b([a-fA-F0-9]{64})\b") {
                return $matches[1].ToLowerInvariant()
            }
        }
        catch { }
    }

    # Check release body for 64-character hex string
    if ($release.body -and $release.body -match "\b([a-fA-F0-9]{64})\b") {
        return $matches[1].ToLowerInvariant()
    }

    return $null
}

foreach ($rel in $releases) {
    $expectedHash = Get-ReleaseHash $rel
    if ($expectedHash -and $expectedHash -eq $localHash) {
        $matchedRelease = $rel
        break
    }
}

Write-Host "  -------------------------------------------------------------" -ForegroundColor DarkGray
Write-Host "   AUDIT VERDICT:" -ForegroundColor White

if ($matchedRelease) {
    if ($matchedRelease.tag_name -eq $latestRelease.tag_name) {
        Write-Host "   [+] VERIFIED: Binary matches the latest official GitHub release ($($matchedRelease.tag_name))." -ForegroundColor Green
        Write-Host "       Integrity: 100% Match $bullet Untampered $bullet Up to date" -ForegroundColor Green
    }
    else {
        Write-Host "   [i] VERIFIED (HISTORICAL): Binary matches official release ($($matchedRelease.tag_name))." -ForegroundColor Cyan
        Write-Host "       Integrity: 100% Match $bullet Untampered" -ForegroundColor Cyan
        Write-Host "       Notice: A newer official release ($($latestRelease.tag_name)) is available on GitHub." -ForegroundColor Yellow
    }
}
else {
    # Check if reported version matches any release tag
    $verMatch = $releases | Where-Object { $_.tag_name.TrimStart('v', 'V') -eq $localVersion } | Select-Object -First 1
    if ($verMatch) {
        $expected = Get-ReleaseHash $verMatch
        Write-Host "   [!] CHECKSUM MISMATCH DETECTED!" -ForegroundColor Red
        Write-Host "       File claims to be: $($verMatch.tag_name)" -ForegroundColor Red
        Write-Host "       Expected SHA-256:  $expected" -ForegroundColor Yellow
        Write-Host "       Actual Local Hash: $localHash" -ForegroundColor Yellow
        Write-Host "       WARNING: This binary does not match the official release artifact!" -ForegroundColor Red
    }
    else {
        Write-Host "   [*] LOCAL / DEVELOPMENT BUILD" -ForegroundColor Yellow
        Write-Host "       The running binary does not match any public GitHub release hash." -ForegroundColor DarkYellow
        Write-Host "       This is expected for self-compiled, test, or pre-release developer builds." -ForegroundColor DarkYellow
    }
}
Write-Host "  =============================================================" -ForegroundColor DarkCyan
Write-Host ""
