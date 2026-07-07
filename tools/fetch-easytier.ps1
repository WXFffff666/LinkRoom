# fetch-easytier.ps1
# Downloads and validates EasyTier Windows x86_64 binaries for LinkRoom.
# Usage: .\tools\fetch-easytier.ps1 [-Version "v2.6.4"] [-OutputDir "src\LinkRoom.Core\Assets\easytier"]

param(
    [string]$Version = "v2.6.4",
    [string]$OutputDir = "src\LinkRoom.Core\Assets\easytier"
)

$ErrorActionPreference = "Stop"

$Arch = "x86_64"
$BaseUrl = "https://github.com/EasyTier/EasyTier/releases/download"
$ZipName = "easytier-windows-$Arch-$Version.zip"
$DownloadUrl = "$BaseUrl/$Version/$ZipName"

# Required files that MUST be present after extraction
$RequiredFiles = @(
    "easytier-core.exe",
    "easytier-cli.exe",
    "easytier-web.exe",
    "easytier-web-embed.exe",
    "wintun.dll",
    "Packet.dll",
    "WinDivert64.sys"
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path "$ScriptDir\.."
$TargetDir = Join-Path $RepoRoot $OutputDir
$TempZip = Join-Path ([System.IO.Path]::GetTempPath()) $ZipName
$TempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "easytier-extract-$PID"

Write-Host "=== LinkRoom EasyTier Fetch Script ===" -ForegroundColor Cyan
Write-Host "Version : $Version"
Write-Host "Arch    : $Arch"
Write-Host "Source  : $DownloadUrl"
Write-Host "Target  : $TargetDir"
Write-Host ""

# Step 1: Download
Write-Host "[1/4] Downloading $ZipName ..." -ForegroundColor Yellow
try {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $TempZip -UseBasicParsing
    $fileSize = (Get-Item $TempZip).Length
    Write-Host "       Downloaded $($fileSize / 1MB) MB" -ForegroundColor Green
}
catch {
    Write-Error "Failed to download from $DownloadUrl : $_"
    exit 1
}

# Step 2: Extract
Write-Host "[2/4] Extracting ..." -ForegroundColor Yellow
try {
    if (Test-Path $TempExtract) { Remove-Item -Recurse -Force $TempExtract }
    Expand-Archive -Path $TempZip -DestinationPath $TempExtract -Force
    Write-Host "       Extracted to $TempExtract" -ForegroundColor Green
}
catch {
    Remove-Item $TempZip -ErrorAction SilentlyContinue
    Write-Error "Failed to extract: $_"
    exit 1
}

# Step 3: Locate files (handle possible subdirectory in zip)
Write-Host "[3/4] Locating required files ..." -ForegroundColor Yellow
$extractRoot = $TempExtract
$subDirs = Get-ChildItem -Path $TempExtract -Directory
if ($subDirs.Count -eq 1) {
    $extractRoot = $subDirs[0].FullName
    Write-Host "       Files found inside subdirectory: $($subDirs[0].Name)" -ForegroundColor Gray
}

$missing = @()
foreach ($file in $RequiredFiles) {
    $found = Get-ChildItem -Path $extractRoot -Filter $file -Recurse
    if (-not $found) {
        $missing += $file
    }
}
if ($missing.Count -gt 0) {
    Write-Error "Missing required files: $($missing -join ', ')"
    Remove-Item $TempZip -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $TempExtract -ErrorAction SilentlyContinue
    exit 1
}
Write-Host "       All $($RequiredFiles.Count) required files present" -ForegroundColor Green

# Step 4: Copy to target (overwrite existing)
Write-Host "[4/4] Copying to $OutputDir ..." -ForegroundColor Yellow
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
}
# Remove old files to ensure clean state
Get-ChildItem -Path $TargetDir -File | Remove-Item -Force
# Copy new files
foreach ($file in $RequiredFiles) {
    $src = Get-ChildItem -Path $extractRoot -Filter $file -Recurse | Select-Object -First 1
    Copy-Item -Path $src.FullName -Destination $TargetDir -Force
    Write-Host "       Copied: $file" -ForegroundColor Gray
}

# Cleanup
Remove-Item $TempZip -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $TempExtract -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== Fetch complete! ===" -ForegroundColor Cyan
Write-Host "Files placed in: $TargetDir" -ForegroundColor White
Get-ChildItem $TargetDir | ForEach-Object { Write-Host "  $($_.Name) ($($_.Length) bytes)" }
