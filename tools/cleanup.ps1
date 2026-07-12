# Cleanup LinkRoom runtime data
param([string]$DataRoot = "")

if (-not $DataRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $portable = Join-Path (Resolve-Path "$scriptDir\..") "LinkRoomData"
    if (Test-Path $portable) { $DataRoot = $portable }
    else { $DataRoot = "$env:LOCALAPPDATA\LinkRoom" }
}

Write-Host "Cleaning $DataRoot ..." -ForegroundColor Yellow
foreach ($sub in @("temp", "logs", "diagnostics")) {
    $p = Join-Path $DataRoot $sub
    if (Test-Path $p) { Remove-Item -Recurse -Force $p; Write-Host "  Removed $sub" }
}
Write-Host "Done." -ForegroundColor Green
