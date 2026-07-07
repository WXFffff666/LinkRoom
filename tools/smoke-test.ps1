# LinkRoom smoke test — two local EasyTier nodes P2P mesh verification
# Usage: powershell -File tools\smoke-test.ps1
# Requires: easytier-core.exe and easytier-cli.exe in %LOCALAPPDATA%\LinkRoom\runtime\2.6.4\

param(
    [string]$RuntimeDir = "$env:LOCALAPPDATA\LinkRoom\runtime\2.6.4",
    [string]$NetworkName = "linkroom-smoke",
    [string]$NetworkSecret = "smoke-secret"
)

$ErrorActionPreference = "Stop"
$Core = Join-Path $RuntimeDir "easytier-core.exe"
$Cli = Join-Path $RuntimeDir "easytier-cli.exe"

if (-not (Test-Path $Core)) { Write-Error "easytier-core.exe not found at $Core"; exit 1 }
if (-not (Test-Path $Cli)) { Write-Error "easytier-cli.exe not found at $Cli"; exit 1 }

Write-Host "=== LinkRoom Smoke Test ===" -ForegroundColor Cyan
Write-Host "Runtime: $RuntimeDir" -ForegroundColor Gray

# Start Node A
Write-Host "[1/4] Starting Node A..." -ForegroundColor Yellow
$nodeA = Start-Process $Core -ArgumentList @(
    "--network-name", $NetworkName,
    "--network-secret", $NetworkSecret,
    "-i", "10.144.144.1",
    "--listeners", "tcp://127.0.0.1:11010",
    "--rpc-portal", "127.0.0.1:15888",
    "--dev-name", "linkroom-smoke-a"
) -PassThru -WindowStyle Hidden

# Start Node B (connects to Node A)
Write-Host "[2/4] Starting Node B..." -ForegroundColor Yellow
$nodeB = Start-Process $Core -ArgumentList @(
    "--network-name", $NetworkName,
    "--network-secret", $NetworkSecret,
    "-i", "10.144.144.2",
    "--listeners", "tcp://127.0.0.1:11011",
    "--rpc-portal", "127.0.0.1:15889",
    "--dev-name", "linkroom-smoke-b",
    "-p", "tcp://127.0.0.1:11010"
) -PassThru -WindowStyle Hidden

# Wait for mesh formation
Write-Host "[3/4] Waiting for P2P mesh (max 30s)..." -ForegroundColor Yellow
$success = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    try {
        $output = & $Cli --output json --rpc-portal 127.0.0.1:15888 peer 2>$null | ConvertFrom-Json
        if ($output.cost -eq "p2p" -or ($output -is [array] -and ($output | Where-Object { $_.cost -eq "p2p" }))) {
            $success = $true
            Write-Host "       P2P mesh established! cost=p2p" -ForegroundColor Green
            break
        }
    } catch { }
}

# Ping test
Write-Host "[4/4] Ping test..." -ForegroundColor Yellow
if ($success) {
    $pingResult = & ping -n 4 10.144.144.2 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "       Ping OK (0% loss)" -ForegroundColor Green
    } else {
        Write-Host "       Ping FAILED" -ForegroundColor Red
        $success = $false
    }
}

# Cleanup
Write-Host "Cleaning up..." -ForegroundColor Gray
if ($nodeA -and !$nodeA.HasExited) { $nodeA.Kill() }
if ($nodeB -and !$nodeB.HasExited) { $nodeB.Kill() }

if ($success) {
    Write-Host "`n=== SMOKE TEST PASSED ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n=== SMOKE TEST FAILED ===" -ForegroundColor Red
    exit 1
}
