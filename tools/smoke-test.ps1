# LinkRoom smoke test — two local EasyTier nodes P2P mesh verification
param(
    [string]$RuntimeDir = "",
    [string]$NetworkName = "linkroom-smoke",
    [string]$NetworkSecret = "smoke-secret"
)

$ErrorActionPreference = "Stop"
if (-not $RuntimeDir) {
    $exeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RuntimeDir = Join-Path (Resolve-Path "$exeDir\..") "LinkRoomData\runtime\2.6.4"
    if (-not (Test-Path $RuntimeDir)) {
        $RuntimeDir = "$env:LOCALAPPDATA\LinkRoom\runtime\2.6.4"
    }
}

$Core = Join-Path $RuntimeDir "easytier-core.exe"
$Cli = Join-Path $RuntimeDir "easytier-cli.exe"

if (-not (Test-Path $Core)) { Write-Error "easytier-core.exe not found at $Core"; exit 1 }
if (-not (Test-Path $Cli)) { Write-Error "easytier-cli.exe not found at $Cli"; exit 1 }

Write-Host "=== LinkRoom Smoke Test ===" -ForegroundColor Cyan
Write-Host "Runtime: $RuntimeDir" -ForegroundColor Gray

Write-Host "[1/4] Starting Node A..." -ForegroundColor Yellow
$nodeA = Start-Process $Core -ArgumentList @(
    "--network-name", $NetworkName,
    "--network-secret", $NetworkSecret,
    "-i", "10.144.144.1",
    "--listeners", "tcp://127.0.0.1:11010",
    "--rpc-portal", "127.0.0.1:15888",
    "--dev-name", "linkroom-smoke-a"
) -PassThru -WindowStyle Hidden

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

Write-Host "[3/4] Waiting for P2P mesh (max 30s)..." -ForegroundColor Yellow
$success = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    try {
        $output = & $Cli --output json --rpc-portal 127.0.0.1:15888 peer 2>$null | ConvertFrom-Json
        if ($output.cost -eq "p2p" -or ($output -is [array] -and ($output | Where-Object { $_.cost -eq "p2p" }))) {
            $success = $true
            Write-Host "       P2P mesh established!" -ForegroundColor Green
            break
        }
    } catch { }
}

Write-Host "[4/4] Ping test..." -ForegroundColor Yellow
if ($success) {
    $pingResult = & ping -n 4 10.144.144.2 2>&1
    if ($LASTEXITCODE -ne 0) { $success = $false; Write-Host "       Ping FAILED" -ForegroundColor Red }
    else { Write-Host "       Ping OK" -ForegroundColor Green }
}

if ($nodeA -and !$nodeA.HasExited) { $nodeA.Kill() }
if ($nodeB -and !$nodeB.HasExited) { $nodeB.Kill() }

if ($success) { Write-Host "`n=== PASSED ===" -ForegroundColor Green; exit 0 }
else { Write-Host "`n=== FAILED ===" -ForegroundColor Red; exit 1 }
