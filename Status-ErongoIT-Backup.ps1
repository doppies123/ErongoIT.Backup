$apiRunning = $false
$webRunning = $false
$agentRunning = $false

try {
    $response = Invoke-WebRequest `
        -Uri "http://localhost:5230/api/health" `
        -TimeoutSec 3 `
        -UseBasicParsing

    $apiRunning = ($response.StatusCode -eq 200)
}
catch {
}

try {
    $response = Invoke-WebRequest `
        -Uri "http://localhost:5165" `
        -TimeoutSec 3 `
        -UseBasicParsing

    $webRunning = ($response.StatusCode -eq 200)
}
catch {
}

$agentRunning = @(Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "dotnet.exe" -and
        $_.CommandLine -like "*ErongoIT.Backup.Agent*"
    }).Count -gt 0

$allRunning = $apiRunning -and $webRunning -and $agentRunning

Clear-Host

Write-Host ""
Write-Host "========================================"
Write-Host "       ErongoIT Backup Services"
Write-Host "========================================"
Write-Host ""

Write-Host "API      : " -NoNewline
if ($apiRunning) {
    Write-Host "RUNNING" -ForegroundColor Green
}
else {
    Write-Host "STOPPED" -ForegroundColor Red
}

Write-Host "           http://localhost:5230"
Write-Host ""

Write-Host "Web      : " -NoNewline
if ($webRunning) {
    Write-Host "RUNNING" -ForegroundColor Green
}
else {
    Write-Host "STOPPED" -ForegroundColor Red
}

Write-Host "           http://localhost:5165"
Write-Host ""

Write-Host "Agent    : " -NoNewline
if ($agentRunning) {
    Write-Host "RUNNING" -ForegroundColor Green
}
else {
    Write-Host "STOPPED" -ForegroundColor Red
}

Write-Host ""
Write-Host "----------------------------------------"

if ($allRunning) {
    Write-Host "Overall  : ONLINE" -ForegroundColor Green
}
elseif (-not $apiRunning -and -not $webRunning -and -not $agentRunning) {
    Write-Host "Overall  : OFFLINE" -ForegroundColor Red
}
else {
    Write-Host "Overall  : PARTIALLY RUNNING" -ForegroundColor Yellow
}

Write-Host "----------------------------------------"
Write-Host ""
Write-Host "Press Enter to close..."
Read-Host
