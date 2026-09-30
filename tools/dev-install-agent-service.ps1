# ErongoIT Backup - developer test install of the Agent as a Windows service.
# Does what the real installer will do: publish, enroll, register service, start.
# Run elevated (the one-liner in chat does that for you).

$ErrorActionPreference = "Stop"

$Repo        = "C:\Users\Raymond\source\repos\ErongoIT.Backup"
$InstallDir  = "C:\Program Files\ErongoIT Backup\Agent"
$ServiceName = "ErongoITBackupAgent"
$Server      = "https://backup.erongoit.com"
$Customer    = "cc66f4bb-91db-468e-8124-f327b433902b"
$Plan        = "b461969c-e8d7-4050-8fad-52fc201dd84a"
$DeviceName  = "TEST-PC-01"
$Folder      = "C:\ErongoIT.Backup\AgentTestData"
$DataDir     = "C:\ProgramData\ErongoIT Backup"

function Step($text) { Write-Host ""; Write-Host "=== $text ===" -ForegroundColor Cyan }

try {
    Step "1. Stop developer Agent (dotnet run) and any old service"
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*ErongoIT.Backup.Agent\*" -and $_.CommandLine -notlike "*Agent.Gui*" } |
        ForEach-Object { Write-Host "Stopping dev Agent PID $($_.ProcessId)"; Stop-Process -Id $_.ProcessId -Force }
    Get-Process -Name "ErongoIT.Backup.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep 2
    }

    Step "2. Publish Agent to $InstallDir"
    dotnet publish "$Repo\ErongoIT.Backup.Agent\ErongoIT.Backup.Agent.csproj" `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -o $InstallDir | Select-Object -Last 3
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
    if (Test-Path "$InstallDir\appsettings.Development.json") { throw "Developer settings were published - aborting." }

    Step "3. Enroll this PC"
    $secure = Read-Host "Admin password for $Server" -AsSecureString
    $plain  = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    & "$InstallDir\ErongoIT.Backup.Agent.exe" --enroll --server $Server --username admin --password $plain `
        --customer $Customer --name $DeviceName --plan $Plan --folder $Folder
    $plain = $null
    if ($LASTEXITCODE -ne 0) { throw "Enrollment failed." }

    Step "4. Register and start Windows service"
    New-Service -Name $ServiceName `
        -BinaryPathName "`"$InstallDir\ErongoIT.Backup.Agent.exe`"" `
        -DisplayName "ErongoIT Backup Agent" `
        -Description "Backs up this PC to the ErongoIT cloud backup server." `
        -StartupType Automatic | Out-Null
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
    Start-Service $ServiceName
    Start-Sleep 15

    Step "5. Result"
    Get-Service $ServiceName | Format-Table Status, Name, DisplayName, StartType -AutoSize
    Write-Host "agent.json (device key is DPAPI-encrypted):"
    Get-Content "$DataDir\agent.json" | ForEach-Object {
        if ($_ -match '"deviceKeyProtected"') { '  "deviceKeyProtected": "<encrypted, ' + $_.Length + ' chars>",' } else { $_ }
    }
    Write-Host ""
    Write-Host "Service log (last 15 lines):"
    Get-ChildItem "$DataDir\logs\agent-*.log" | Sort-Object LastWriteTime | Select-Object -Last 1 |
        ForEach-Object { Get-Content $_.FullName -Tail 15 }
    Write-Host ""
    Write-Host "DONE" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Read-Host "Press Enter to close"
