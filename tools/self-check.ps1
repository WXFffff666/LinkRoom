# LinkRoom self-check
param([string]$DataRoot = "")

$pass = 0; $fail = 0
if (-not $DataRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $portable = Join-Path (Resolve-Path "$scriptDir\..") "LinkRoomData"
    $DataRoot = if (Test-Path $portable) { $portable } else { "$env:LOCALAPPDATA\LinkRoom" }
}

$runtime = Join-Path $DataRoot "runtime\2.6.4"
Write-Host "=== LinkRoom Self-Check ===" -ForegroundColor Cyan
Write-Host "Data: $DataRoot" -ForegroundColor Gray

foreach ($f in @("easytier-core.exe", "easytier-cli.exe", "wintun.dll")) {
    $p = Join-Path $runtime $f
    if (Test-Path $p) { Write-Host "  OK  $f" -ForegroundColor Green; $pass++ }
    else { Write-Host "  FAIL $f" -ForegroundColor Red; $fail++ }
}

try {
    if ((New-Object System.Net.NetworkInformation.Ping).Send("8.8.8.8", 2000).Status -eq "Success") {
        Write-Host "  OK  Network" -ForegroundColor Green; $pass++
    } else { Write-Host "  FAIL Network" -ForegroundColor Red; $fail++ }
} catch { Write-Host "  FAIL Network" -ForegroundColor Red; $fail++ }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Host "  $(if ($admin) {'OK'} else {'WARN'})  Admin=$admin" -ForegroundColor $(if ($admin) {"Green"} else {"Yellow"})

Write-Host "`n$pass passed / $fail failed" -ForegroundColor $(if ($fail -eq 0) {"Green"} else {"Red"})
