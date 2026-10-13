$ErrorActionPreference = 'Stop'
$monitorExe = Join-Path $PSScriptRoot 'dist/CodexMeter.exe'
if (-not (Test-Path -LiteralPath $monitorExe)) { throw '请先构建 dist/CodexMeter.exe。' }
$legacySettings = Join-Path $env:LOCALAPPDATA 'CodexMonitor/settings.json'
$meterSettings = Join-Path $env:LOCALAPPDATA 'CodexMeter/settings.json'
if ((Test-Path -LiteralPath $legacySettings) -and -not (Test-Path -LiteralPath $meterSettings)) {
    New-Item -ItemType Directory -Path (Split-Path $meterSettings) -Force | Out-Null
    Copy-Item -LiteralPath $legacySettings -Destination $meterSettings
}
$monitorRunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (-not (Test-Path -LiteralPath $monitorRunKey)) { New-Item -Path $monitorRunKey | Out-Null }
New-ItemProperty -Path $monitorRunKey -Name 'CodexMeter' -Value ('"' + $monitorExe + '"') -PropertyType String -Force | Out-Null
Remove-ItemProperty -Path $monitorRunKey -Name 'CodexMonitor' -ErrorAction SilentlyContinue
$legacyExe = Join-Path $PSScriptRoot 'dist/CodexMonitor.exe'
Get-Process CodexMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $legacyExe } | Stop-Process
Start-Process -FilePath $monitorExe -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
Write-Output 'Codex 用量条 已启动，已启用当前用户的开机自动启动。'
