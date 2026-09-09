$ErrorActionPreference = 'Stop'
$url = 'https://api.github.com/repos/TalviFox/WireFox/releases/latest'
$release = Invoke-RestMethod -Uri $url -UseBasicParsing
$sumsAsset = $release.assets | Where-Object { $_.name -eq 'WireFox.exe.sha256' } | Select-Object -First 1
if ($sumsAsset) {
    Write-Host "Found asset: $($sumsAsset.name)"
    $checksumData = Invoke-RestMethod -Uri $sumsAsset.browser_download_url -UseBasicParsing
    Write-Host "Type: $($checksumData.GetType().FullName)"
    $checksumText = ""
    if ($checksumData -is [byte[]]) {
        Write-Host "It is a byte array."
        $checksumText = [System.Text.Encoding]::UTF8.GetString($checksumData)
    } else {
        $checksumText = [string]$checksumData
    }
    Write-Host "Text: $checksumText"
    if ($checksumText -match "([a-fA-F0-9]{64})\s+.*WireFox\.exe") {
        Write-Host "Match 1: $($matches[1])"
    } elseif ($checksumText -match "\b([a-fA-F0-9]{64})\b") {
        Write-Host "Match 2: $($matches[1])"
    } else {
        Write-Host "No match!"
    }
} else {
    Write-Host "No asset found."
}
