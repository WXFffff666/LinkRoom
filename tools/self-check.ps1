# LinkRoom Self-Check Script
# Verifies all dependencies, runtime, and network connectivity.
# Usage: powershell -File tools\self-check.ps1

param([switch]$ExitOnFail)

$ErrorActionPreference = "Continue"
$pass = 0; $fail = 0; $warn = 0

function Check($name, $condition, $failMsg) {
    if ($condition) { Write-Host "  [PASS] $name" -ForegroundColor Green; $script:pass++ }
    else { Write-Host "  [FAIL] $name — $failMsg" -ForegroundColor Red; $script:fail++ }
}

function Warn($name, $condition, $msg) {
    if ($condition) { Write-Host "  [PASS] $name" -ForegroundColor Green; $script:pass++ }
    else { Write-Host "  [WARN] $name — $msg" -ForegroundColor Yellow; $script:warn++ }
}

Write-Host "`n=== LinkRoom Self-Check ===`n" -ForegroundColor Cyan

# 1. Admin rights
Check "管理员权限" (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) "需要管理员权限安装 Wintun 驱动"

# 2. .NET Runtime
Check ".NET 8 Runtime" ((Get-Command "dotnet" -ErrorAction SilentlyContinue) -and (dotnet --version 2>$null | Select-String "8.")) "需要 .NET 8.0 运行时"

# 3. EasyTier Runtime Files
$rt = "$env:LOCALAPPDATA\LinkRoom\runtime\2.6.4"
Check "easytier-core.exe" (Test-Path "$rt\easytier-core.exe") "运行时未安装，请先运行 LinkRoom.exe"
Check "easytier-cli.exe" (Test-Path "$rt\easytier-cli.exe") "运行时未安装"
Check "wintun.dll" (Test-Path "$rt\wintun.dll") "Wintun 驱动未安装"
Check "Packet.dll" (Test-Path "$rt\Packet.dll") "Packet.dll 未安装"
Check "WinDivert64.sys" (Test-Path "$rt\WinDivert64.sys") "WinDivert 驱动未安装"

# 4. EasyTier version
if (Test-Path "$rt\easytier-core.exe") {
    $v = & "$rt\easytier-core.exe" --version 2>$null
    Check "EasyTier 版本" ($v -match "2\.6") "版本不匹配: $v"
} else { Check "EasyTier 版本" $false "核心文件不存在" }

# 5. Network
$ping1 = Test-Connection -ComputerName 8.8.8.8 -Count 1 -Quiet -TimeoutSeconds 5
Check "互联网连接" $ping1 "无法访问互联网"

# 6. STUN server
$ping2 = Test-Connection -ComputerName stun.l.google.com -Count 1 -Quiet -TimeoutSeconds 5
Warn "STUN 服务器" $ping2 "NAT 类型检测可能失败"

# 7. Local network adapter
$adapters = Get-NetAdapter -Physical | Where-Object Status -eq "Up"
Check "网络适配器" ($adapters.Count -gt 0) "没有活动的网络适配器"

# 8. Firewall
$fw = Get-NetFirewallProfile -PolicyStore ActiveStore | Where-Object Enabled -eq "True"
Check "Windows 防火墙" ($fw.Count -gt 0) "防火墙未启用（P2P 可能需要防火墙规则）"

# 9. Disk space (need ~200 MB for runtime)
$free = (Get-PSDrive C).Free / 1MB
Check "磁盘空间 (>200MB)" ($free -gt 200) "可用空间不足: $([math]::Round($free)) MB"

# Summary
Write-Host "`n=== Results: $pass passed, $warn warnings, $fail failed ===`n" -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Red" })

if ($ExitOnFail -and $fail -gt 0) { exit 1 } else { exit 0 }