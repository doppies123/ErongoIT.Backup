$ErrorActionPreference = "Continue"

$Root = "C:\Users\Raymond\source\repos\ErongoIT.Backup"
$LogDir = Join-Path $Root "logs"

New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "     Starting ErongoIT Backup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Stop any existing ErongoIT Backup processes first.
$existing = @(Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "dotnet.exe" -and
        $_.CommandLine -like "*ErongoIT.Backup*"
    })

foreach ($process in $existing) {
    Write-Host "Stopping existing process PID $($process.ProcessId)..."
    Stop-Process `
        -Id $process.ProcessId `
        -Force `
        -ErrorAction SilentlyContinue
}

if ($existing.Count -gt 0) {
    Start-Sleep -Seconds 2
}

# Clear old logs.
Remove-Item "$LogDir\API.out.log" -Force -ErrorAction SilentlyContinue
Remove-Item "$LogDir\API.err.log" -Force -ErrorAction SilentlyContinue
Remove-Item "$LogDir\Agent.out.log" -Force -ErrorAction SilentlyContinue
Remove-Item "$LogDir\Agent.err.log" -Force -ErrorAction SilentlyContinue
Remove-Item "$LogDir\Web.out.log" -Force -ErrorAction SilentlyContinue
Remove-Item "$LogDir\Web.err.log" -Force -ErrorAction SilentlyContinue

Write-Host "Starting API..."

Start-Process `
    -FilePath "dotnet.exe" `
    -ArgumentList "run --project `"$Root\ErongoIT.Backup.Api`" --launch-profile http" `
    -WorkingDirectory $Root `
    -WindowStyle Hidden `
    -RedirectStandardOutput "$LogDir\API.out.log" `
    -RedirectStandardError "$LogDir\API.err.log"

Write-Host "Starting Agent..."

Start-Process `
    -FilePath "dotnet.exe" `
    -ArgumentList "run --project `"$Root\ErongoIT.Backup.Agent`" --launch-profile ErongoIT.Backup.Agent" `
    -WorkingDirectory $Root `
    -WindowStyle Hidden `
    -RedirectStandardOutput "$LogDir\Agent.out.log" `
    -RedirectStandardError "$LogDir\Agent.err.log"

Write-Host "Starting Web..."

Start-Process `
    -FilePath "dotnet.exe" `
    -ArgumentList "run --project `"$Root\ErongoIT.Backup.Web`" --launch-profile http" `
    -WorkingDirectory $Root `
    -WindowStyle Hidden `
    -RedirectStandardOutput "$LogDir\Web.out.log" `
    -RedirectStandardError "$LogDir\Web.err.log"

Write-Host ""
Write-Host "Waiting for services to start..." -ForegroundColor Yellow

$apiReady = $false
$webReady = $false

for ($i = 1; $i -le 30; $i++) {

    Start-Sleep -Seconds 1

    if (-not $apiReady) {
        try {
            $response = Invoke-WebRequest `
                -Uri "http://localhost:5230/api/health" `
                -TimeoutSec 1 `
                -UseBasicParsing

            if ($response.StatusCode -eq 200) {
                $apiReady = $true
            }
        }
        catch {
        }
    }

    if (-not $webReady) {
        try {
            $response = Invoke-WebRequest `
                -Uri "http://localhost:5165" `
                -TimeoutSec 1 `
                -UseBasicParsing

            if ($response.StatusCode -eq 200) {
                $webReady = $true
            }
        }
        catch {
        }
    }

    if ($apiReady -and $webReady) {
        break
    }
}

Write-Host ""

if ($apiReady) {
    Write-Host "API      : RUNNING  http://localhost:5230" -ForegroundColor Green
}
else {
    Write-Host "API      : FAILED   http://localhost:5230" -ForegroundColor Red
}

if ($webReady) {
    Write-Host "Web      : RUNNING  http://localhost:5165" -ForegroundColor Green
}
else {
    Write-Host "Web      : FAILED   http://localhost:5165" -ForegroundColor Red
}

$agentRunning = @(Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "dotnet.exe" -and
        $_.CommandLine -like "*ErongoIT.Backup.Agent*"
    }).Count -gt 0

if ($agentRunning) {
    Write-Host "Agent    : RUNNING" -ForegroundColor Green
}
else {
    Write-Host "Agent    : FAILED" -ForegroundColor Red
}

Write-Host ""

if ($apiReady -and $webReady -and $agentRunning) {
    Write-Host "ErongoIT Backup is ONLINE." -ForegroundColor Green
}
else {
    Write-Host "ErongoIT Backup did not start completely." -ForegroundColor Red
    Write-Host ""
    Write-Host "Check the logs in:"
    Write-Host "$LogDir"
}

Write-Host ""
Write-Host "Press Enter to close..."
Read-Host
