# Builds installer\output\ErongoIT-Backup-Setup-<version>.exe
#   1. publishes Agent + GUI (self-contained, win-x64) into installer\stage
#   2. checks no developer secrets were published
#   3. compiles installer\ErongoIT.Backup.iss with Inno Setup (ISCC.exe)
#
# Usage (MobaXterm local tab):
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build-installer.ps1 [-Version 1.1.0]

param([string]$Version = "1.1.0")

$ErrorActionPreference = "Stop"
$Repo   = Split-Path -Parent $PSScriptRoot
$Stage  = Join-Path $Repo "installer\stage"
$Output = Join-Path $Repo "installer\output"

function Step($t) { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Cyan }

Step "1. Locate Inno Setup"
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install --id JRSoftware.InnoSetup -e" }
Write-Host $iscc

Step "2. Clean stage"
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Path "$Stage\Agent", "$Stage\Gui" | Out-Null

Step "3. Publish Agent (self-contained)"
dotnet publish "$Repo\ErongoIT.Backup.Agent\ErongoIT.Backup.Agent.csproj" `
    -c Release -r win-x64 --self-contained true -p:Version=$Version -o "$Stage\Agent" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Agent publish failed." }

Step "4. Publish GUI (self-contained)"
dotnet publish "$Repo\ErongoIT.Backup.Agent.Gui\ErongoIT.Backup.Agent.Gui.csproj" `
    -c Release -r win-x64 --self-contained true -p:Version=$Version -o "$Stage\Gui" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "GUI publish failed." }

Step "5. Security check (no developer settings or passwords)"
$bad = Get-ChildItem $Stage -Recurse -File |
    Where-Object { $_.Name -like "appsettings.Development*.json" -or $_.Name -like "agentgui.*.json" -or $_.Name -eq "agent.json" }
if ($bad) { $bad | ForEach-Object { Write-Host "  FOUND: $($_.FullName)" -ForegroundColor Red }; throw "Developer files were published - aborting." }
$leaks = Get-ChildItem $Stage -Recurse -File -Include *.json | Select-String -Pattern '"(Api)?Password"\s*:\s*"[^"]+' -List
if ($leaks) { $leaks | ForEach-Object { Write-Host "  PASSWORD IN: $($_.Path)" -ForegroundColor Red }; throw "A password was found in published files - aborting." }
Write-Host "OK"

Step "6. Compile installer"
& $iscc "/DAppVersion=$Version" "/Q" "$Repo\installer\ErongoIT.Backup.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

$exe = Get-ChildItem $Output -Filter "ErongoIT-Backup-Setup-$Version.exe" | Select-Object -First 1
Step "DONE"
"{0}  ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)
