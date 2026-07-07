# LinkRoom cleanup script
# Stops any running LinkRoom instance and removes Wintun adapter + runtime data.
# Run as Administrator.

$ErrorActionPreference = "Continue"
Write-Host "=== LinkRoom Cleanup ===" -ForegroundColor Cyan

# Kill running instances
$procs = Get-Process -Name "LinkRoom" -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "Stopping LinkRoom..." -ForegroundColor Yellow
    $procs | Stop-Process -Force
}

# Remove Wintun adapter (if present)
Write-Host "Removing Wintun adapter..." -ForegroundColor Yellow
$adapters = Get-NetAdapter -Name "*linkroom*" -ErrorAction SilentlyContinue
if ($adapters) {
    $adapters | Remove-NetAdapter -Confirm:$false
}

# Remove runtime data
$appDir = "$env:LOCALAPPDATA\LinkRoom"
if (Test-Path $appDir) {
    Write-Host "Removing $appDir..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $appDir
}

Write-Host "=== Cleanup complete ===" -ForegroundColor Green
