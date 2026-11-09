param(
    [switch]$KeepConfig,
    [switch]$Silent
)

$ErrorActionPreference = 'Continue'

$target = Join-Path $env:LOCALAPPDATA 'Programs\BatteryBar'
$configDir = Join-Path $env:APPDATA 'BatteryBar'

Write-Host '==> 停止 BatteryBar' -ForegroundColor Cyan
Get-Process BatteryBar -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

Write-Host '==> 移除开机启动项' -ForegroundColor Cyan
try {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Get-ItemProperty -Path $runKey -Name 'BatteryBar' -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name 'BatteryBar' -ErrorAction SilentlyContinue
    }
} catch { }

Write-Host '==> 删除快捷方式' -ForegroundColor Cyan
$desktop = [Environment]::GetFolderPath('Desktop')
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
foreach ($lnk in @((Join-Path $desktop 'BatteryBar 电池助手.lnk'), (Join-Path $startMenu 'BatteryBar 电池助手.lnk'))) {
    if (Test-Path $lnk) { Remove-Item $lnk -Force -ErrorAction SilentlyContinue }
}

Write-Host '==> 删除安装目录' -ForegroundColor Cyan
if (Test-Path $target) { Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue }

$deleteConfig = $KeepConfig -eq $false
if (-not $Silent -and -not $KeepConfig) {
    $answer = Read-Host '是否同时删除配置文件与设备列表？(y/N)'
    $deleteConfig = ($answer -eq 'y' -or $answer -eq 'Y')
}
if ($deleteConfig -and (Test-Path $configDir)) {
    Remove-Item $configDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host '==> 已删除配置' -ForegroundColor Cyan
}

Write-Host '==> 卸载完成' -ForegroundColor Green
