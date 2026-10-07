$ErrorActionPreference = "SilentlyContinue"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "      Stopping ErongoIT Backup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ports used by ErongoIT Backup.
$ports = @(5230, 5165)

foreach ($port in $ports) {

    Write-Host "Checking port $port..."

    $connections = @(Get-NetTCPConnection `
        -LocalPort $port `
        -State Listen `
        -ErrorAction SilentlyContinue)

    foreach ($connection in $connections) {

        $processId = $connection.OwningProcess

        if ($processId -gt 0) {
            $process = Get-Process `
                -Id $processId `
                -ErrorAction SilentlyContinue

            if ($process) {
                Write-Host "Stopping $($process.ProcessName) PID $processId on port $port..."
                Stop-Process `
                    -Id $processId `
                    -Force `
                    -ErrorAction SilentlyContinue
            }
        }
    }
}

# Stop the Agent and any remaining ErongoIT Backup dotnet processes.
$agentProcesses = @(Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "dotnet.exe" -and
        (
            $_.CommandLine -like "*ErongoIT.Backup.Agent*" -or
            $_.CommandLine -like "*ErongoIT.Backup.Api*" -or
            $_.CommandLine -like "*ErongoIT.Backup.Web*"
        )
    })

foreach ($process in $agentProcesses) {

    Write-Host "Stopping ErongoIT process PID $($process.ProcessId)..."

    Stop-Process `
        -Id $process.ProcessId `
        -Force `
        -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 2

Write-Host ""
Write-Host "Checking final status..." -ForegroundColor Yellow
Write-Host ""

& "C:\Users\Raymond\source\repos\ErongoIT.Backup\Status-ErongoIT-Backup.ps1"
