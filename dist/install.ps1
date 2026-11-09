param(
    [switch]$NoStart,
    [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'BatteryBar.exe'
if (-not (Test-Path $source)) { throw "未找到 BatteryBar.exe，请在 dist 目录中运行本脚本。" }

$target = Join-Path $env:LOCALAPPDATA 'Programs\BatteryBar'
$exe = Join-Path $target 'BatteryBar.exe'

Write-Host "==> 安装到 $target" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $target | Out-Null
Get-Process BatteryBar -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Copy-Item $source $exe -Force

# BLE 广播助手（苹果 / 小米耳机需要，无 Python 依赖）
$agent = Join-Path $PSScriptRoot 'tools\BleAgent'
if (Test-Path $agent) {
    $agentTarget = Join-Path $target 'tools'
    New-Item -ItemType Directory -Force -Path $agentTarget | Out-Null
    Copy-Item (Join-Path $agent '*') $agentTarget -Recurse -Force
    Write-Host '==> 已复制 BLE 广播助手' -ForegroundColor Cyan
}

function New-Shortcut([string]$path, [string]$targetFile) {
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($path)
    $sc.TargetPath = $targetFile
    $sc.WorkingDirectory = Split-Path -Parent $targetFile
    $sc.Description = 'BatteryBar 电池助手 - 任务栏电量'
    $sc.IconLocation = $targetFile + ',0'
    $sc.Save()
}

if (-not $NoShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    New-Shortcut (Join-Path $desktop 'BatteryBar 电池助手.lnk') $exe
    New-Shortcut (Join-Path $startMenu 'BatteryBar 电池助手.lnk') $exe
    Write-Host '==> 已创建桌面 / 开始菜单快捷方式' -ForegroundColor Cyan
}

if (-not $NoStart) {
    Start-Process -FilePath $exe
    Write-Host '==> 已启动 BatteryBar（图标在任务栏右下角的托盘里）' -ForegroundColor Green
}

Write-Host ''
Write-Host '提示：Windows 11 默认把新图标收进「隐藏的图标」，' -ForegroundColor Yellow
Write-Host '      在 设置 → 个性化 → 任务栏 → 其他系统托盘图标 中打开 BatteryBar 即可固定。' -ForegroundColor Yellow
