# 用 Git Credential Manager 里缓存的 GitHub 凭据，创建 / 更新 Release 并上传 zip
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File app\tools\create_release.ps1
#   powershell -ExecutionPolicy Bypass -File app\tools\create_release.ps1 -Tag v1.0.1
#
# 注意：请求体必须按 UTF-8 字节发送，否则中文会被写成 "?"（PowerShell 默认按 ANSI 编码）

param(
    [string]$Tag = '',
    [string]$Repo = 'Frednko/BTDeviceBatteryView',
    [string]$NotesFile = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# ── 版本 / tag：默认取本地最新 tag ───────────────────────────────────────────
if (-not $Tag) {
    $Tag = (git -C $root describe --tags --abbrev=0 2>$null)
    if (-not $Tag) { throw "没有找到 tag，请先运行 app\release.ps1 -Tag，或用 -Tag 指定" }
}
$version = $Tag.TrimStart('v')

# ── 发布说明 ─────────────────────────────────────────────────────────────────
if ($NotesFile -and (Test-Path $NotesFile)) {
    $notes = Get-Content $NotesFile -Raw -Encoding UTF8
} else {
    $notes = @"
Windows 托盘电量显示工具。

- 自动读取罗技 / PS 手柄 / 通用 HID / BLE 广播设备电量
- 任务栏图标显示电量，低电量提醒，充电时显示闪电标志
- 解压后运行 install.ps1 安装，或直接运行 BatteryBar.exe

详见 README.md 与 docs/device-presets.md
"@
}

# ── 凭据 ─────────────────────────────────────────────────────────────────────
$env:GIT_TERMINAL_PROMPT = '0'
$out = "protocol=https`nhost=github.com`n`n" | git -C $root credential fill 2>&1
$token = ($out | Select-String -Pattern '^password=(.+)$').Matches.Groups[1].Value
if (-not $token) { throw "未取到 GitHub 令牌（GCM 里没有缓存）" }

$headers = @{
    Authorization          = "token $token"
    Accept                 = 'application/vnd.github+json'
    'User-Agent'           = 'BatteryBar-Release'
    'X-GitHub-Api-Version' = '2022-11-28'
}

function Send-Json([string]$Method, [string]$Uri, [hashtable]$Payload) {
    $json = $Payload | ConvertTo-Json -Depth 5
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)   # 关键：UTF-8 字节
    return Invoke-RestMethod -Uri $Uri -Headers $headers -Method $Method -Body $bytes `
        -ContentType 'application/json; charset=utf-8'
}

# ── 创建或更新 release ──────────────────────────────────────────────────────
$release = $null
try { $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers }
catch { $release = $null }

if ($release) {
    Write-Host "更新已有 release: $Tag" -ForegroundColor Cyan
    $release = Send-Json 'Patch' "https://api.github.com/repos/$Repo/releases/$($release.id)" @{
        name = "BatteryBar $version"
        body = $notes
    }
} else {
    Write-Host "创建 release: $Tag" -ForegroundColor Cyan
    $release = Send-Json 'Post' "https://api.github.com/repos/$Repo/releases" @{
        tag_name   = $Tag
        name       = "BatteryBar $version"
        body       = $notes
        draft      = $false
        prerelease = $false
    }
}
Write-Host "Release: $($release.html_url)"

# ── 上传附件（已存在同名则先删）────────────────────────────────────────────
$zip = Join-Path $root "dist\release\BatteryBar-$version-win-x64.zip"
if (-not (Test-Path $zip)) { throw "找不到 $zip，请先运行 app\release.ps1" }
$name = Split-Path $zip -Leaf

$assets = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/$($release.id)/assets" -Headers $headers
foreach ($a in $assets) {
    if ($a.name -eq $name) {
        Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/assets/$($a.id)" -Headers $headers -Method Delete | Out-Null
        Write-Host "已删除同名旧附件" -ForegroundColor DarkGray
    }
}

$asset = Invoke-RestMethod -Uri "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$name" `
    -Headers $headers -Method Post -InFile $zip -ContentType 'application/zip'
Write-Host ("上传完成: {0} ({1:N1} MB)" -f $asset.name, ($asset.size / 1MB))
Write-Host "下载地址: $($asset.browser_download_url)"
