# Publishes a new agent version so installed PCs offer it as an update.
#
#   1. builds installer\output\ErongoIT-Backup-Setup-<version>.exe (unless -SkipBuild)
#   2. uploads it to the VPS in 2 MB pieces (resumable: just run again if the link drops)
#   3. checks the SHA-256 on the VPS, then publishes updates/latest.json
#
# Installed PCs check every 6 hours (and Settings > Updates > Check for updates)
# and ask the user "Update to version x.y.z?".
#
# Usage (PowerShell, in the repo folder):
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\publish-update.ps1 -Version 1.4.1
#
# Before publishing: add a "## <version>" section at the top of CHANGELOG.md;
# its text is shown to users in the update popup.

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$SkipBuild,
    [string]$Server = "root@159.65.18.148",
    [string]$Key = "$env:USERPROFILE\.ssh\id_ed25519",
    [string]$RemoteDir = "/opt/erongoit-backup/updates"
)

$ErrorActionPreference = "Stop"
$Repo   = Split-Path -Parent $PSScriptRoot
$Output = Join-Path $Repo "installer\output"
$File   = "ErongoIT-Backup-Setup-$Version.exe"
$Local  = Join-Path $Output $File
$Ssh    = @("-i", $Key, "-o", "ServerAliveInterval=15", "-o", "ServerAliveCountMax=4", "-o", "ConnectTimeout=20")

function Step($t) { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Cyan }
function Remote([string]$command) {
    $result = & ssh @Ssh $Server $command
    if ($LASTEXITCODE -ne 0) { throw "Remote command failed: $command" }
    return $result
}

$parsedVersion = $null
if (-not [version]::TryParse($Version, [ref]$parsedVersion)) {
    throw "Version must look like 1.4.1"
}

# ------------------------------------------------------------------
Step "1. Installer"
if (-not $SkipBuild -or -not (Test-Path $Local)) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build-installer.ps1") -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
}
if (-not (Test-Path $Local)) { throw "Installer not found: $Local" }

$Sha  = (Get-FileHash $Local -Algorithm SHA256).Hash.ToLowerInvariant()
$Size = (Get-Item $Local).Length
"{0}  {1:N1} MB  sha256 {2}" -f $File, ($Size / 1MB), $Sha

# ------------------------------------------------------------------
Step "2. Release notes from CHANGELOG.md"
$Notes = ""
$Changelog = Join-Path $Repo "CHANGELOG.md"
if (Test-Path $Changelog) {
    $inSection = $false
    $lines = foreach ($line in Get-Content $Changelog -Encoding UTF8) {
        if ($line -match '^##\s+(\S+)') {
            if ($inSection) { break }
            $inSection = ($Matches[1] -eq $Version)
            continue
        }
        if ($inSection) { $line }
    }
    $Notes = ($lines -join "`n").Trim()
}
if ($Notes) { $Notes } else { Write-Host "No '## $Version' section in CHANGELOG.md - the popup will show no notes." -ForegroundColor Yellow }

# ------------------------------------------------------------------
Step "3. Split into 2 MB pieces"
$PartsDir = Join-Path $Output "parts-$Version"
New-Item -ItemType Directory -Force -Path $PartsDir | Out-Null
$PartSize = 2MB
$buffer = New-Object byte[] $PartSize
$stream = [System.IO.File]::OpenRead($Local)
$index = 0
try {
    while (($read = $stream.Read($buffer, 0, $PartSize)) -gt 0) {
        $name = "part-{0:D4}" -f $index
        $path = Join-Path $PartsDir $name
        if (-not (Test-Path $path) -or (Get-Item $path).Length -ne $read) {
            $out = [System.IO.File]::Create($path)
            $out.Write($buffer, 0, $read)
            $out.Close()
        }
        $index++
    }
} finally { $stream.Close() }
"{0} pieces" -f $index

# ------------------------------------------------------------------
Step "4. Upload (resumable)"
$RemoteParts = "$RemoteDir/parts-$Version"
Remote "mkdir -p '$RemoteParts'" | Out-Null

$existing = @{}
foreach ($line in (Remote "cd '$RemoteParts' && stat -c '%n %s' part-* 2>/dev/null; true")) {
    $p = $line -split ' '
    if ($p.Count -eq 2) { $existing[$p[0]] = [long]$p[1] }
}

$parts = Get-ChildItem $PartsDir -Filter "part-*" | Sort-Object Name
$done = 0
foreach ($part in $parts) {
    $done++
    if ($existing.ContainsKey($part.Name) -and $existing[$part.Name] -eq $part.Length) { continue }

    $ok = $false
    for ($attempt = 1; $attempt -le 5 -and -not $ok; $attempt++) {
        Write-Host ("{0} ({1}/{2}) attempt {3}" -f $part.Name, $done, $parts.Count, $attempt)
        & scp @Ssh -q $part.FullName "${Server}:$RemoteParts/$($part.Name)"
        $ok = ($LASTEXITCODE -eq 0)
        if (-not $ok) { Start-Sleep -Seconds 5 }
    }
    if (-not $ok) { throw "Upload of $($part.Name) failed. Run the script again with -SkipBuild to resume." }
}

# ------------------------------------------------------------------
Step "5. Assemble and verify on the VPS"
$json = [ordered]@{
    version     = $Version
    fileName    = $File
    sha256      = $Sha
    sizeBytes   = $Size
    releasedUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    notes       = $Notes
} | ConvertTo-Json
$LocalJson = Join-Path $Output "latest.json"
[System.IO.File]::WriteAllText($LocalJson, $json, (New-Object System.Text.UTF8Encoding($false)))

& scp @Ssh -q $LocalJson "${Server}:$RemoteDir/latest.json.new"
if ($LASTEXITCODE -ne 0) { throw "Upload of latest.json failed." }

$check = Remote ("cd '$RemoteDir' && cat parts-$Version/part-* > '$File.tmp' && " +
                 "sha256sum '$File.tmp' | cut -d' ' -f1")
if (($check | Select-Object -Last 1).Trim() -ne $Sha) { throw "Checksum on the VPS does not match. Run again with -SkipBuild." }

# The installer goes live first, latest.json last: PCs never see a
# version whose file is incomplete.
Remote ("cd '$RemoteDir' && mv '$File.tmp' '$File' && mv latest.json.new latest.json && rm -rf 'parts-$Version' && " +
        "ls -1t ErongoIT-Backup-Setup-*.exe | tail -n +3 | xargs -r rm -f && ls -lh") | ForEach-Object { $_ }

Remove-Item $PartsDir -Recurse -Force

Step "DONE"
"Version $Version is published. Installed PCs offer the update within 6 hours, or at once via Settings > Updates > Check for updates."
