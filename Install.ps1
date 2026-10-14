$ErrorActionPreference = 'Stop'
$monitorExe = Join-Path $PSScriptRoot 'dist/CodexMeter.exe'
if (-not (Test-Path -LiteralPath $monitorExe)) { throw 'Build dist/CodexMeter.exe first.' }
$legacySettings = Join-Path $env:LOCALAPPDATA 'CodexMonitor/settings.json'
$meterSettings = Join-Path $env:LOCALAPPDATA 'CodexMeter/settings.json'
if ((Test-Path -LiteralPath $legacySettings) -and -not (Test-Path -LiteralPath $meterSettings)) {
    New-Item -ItemType Directory -Path (Split-Path $meterSettings) -Force | Out-Null
    Copy-Item -LiteralPath $legacySettings -Destination $meterSettings
}
$monitorRunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (-not (Test-Path -LiteralPath $monitorRunKey)) { New-Item -Path $monitorRunKey | Out-Null }
New-ItemProperty -Path $monitorRunKey -Name 'CodexMeter' -Value ('"' + $monitorExe + '"') -PropertyType String -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $meterSettings) -Force | Out-Null
$settings = if (Test-Path -LiteralPath $meterSettings) { Get-Content -LiteralPath $meterSettings -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
$settings | Add-Member -NotePropertyName AutoStartDisabled -NotePropertyValue $false -Force
$settings | ConvertTo-Json -Compress | Set-Content -LiteralPath $meterSettings -Encoding UTF8
Remove-ItemProperty -Path $monitorRunKey -Name 'CodexMonitor' -ErrorAction SilentlyContinue
$legacyExe = Join-Path $PSScriptRoot 'dist/CodexMonitor.exe'
Get-Process CodexMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $legacyExe } | Stop-Process
Start-Process -FilePath $monitorExe -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
Write-Output 'CodexMeter started and current-user auto start is enabled.'
