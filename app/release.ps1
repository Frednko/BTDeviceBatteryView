# 打包发布：构建 → 组装发布目录 → 生成 zip → （可选）打 tag / 上传 GitHub Release
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File app\release.ps1
#   powershell -ExecutionPolicy Bypass -File app\release.ps1 -Version 1.0.1 -Tag
#   powershell -ExecutionPolicy Bypass -File app\release.ps1 -Tag -Upload      # 需要 gh CLI 且已登录
#
# 产物：
#   dist\release\BatteryBar-<版本>-win-x64\        发布目录（可直接拷走运行）
#   dist\release\BatteryBar-<版本>-win-x64.zip     分发用压缩包

param(
    [string]$Version = '',
    [switch]$Tag,
    [switch]$Upload,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$appDir = $PSScriptRoot
$root = Split-Path -Parent $appDir
$dist = Join-Path $root 'dist'

# ── 版本号：默认取 AssemblyInfo.cs 里的 AssemblyVersion ──────────────────────
if (-not $Version) {
    $info = Get-Content (Join-Path $appDir 'src\AssemblyInfo.cs') -Encoding UTF8
    $line = $info | Where-Object { $_ -match 'AssemblyVersion\("([0-9.]+)"\)' } | Select-Object -First 1
    if ($line -match 'AssemblyVersion\("([0-9.]+)"\)') { $Version = $Matches[1] }
    else { $Version = '1.0.0' }
}
# 1.0.0.0 → 1.0.0
$Version = ($Version -replace '\.0$', '')
$tagName = "v$Version"

Write-Host "==> 版本: $Version (tag: $tagName)" -ForegroundColor Cyan

# ── 1. 构建 ──────────────────────────────────────────────────────────────────
if (-not $NoBuild) {
    & (Join-Path $appDir 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "构建失败" }
}

$exe = Join-Path $dist 'BatteryBar.exe'
if (-not (Test-Path $exe)) { throw "找不到 $exe，请先构建" }

# ── 2. 组装发布目录 ──────────────────────────────────────────────────────────
$releaseRoot = Join-Path $dist 'release'
$stage = Join-Path $releaseRoot "BatteryBar-$Version-win-x64"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $exe (Join-Path $stage 'BatteryBar.exe') -Force

# BLE 广播助手（苹果 / 小米耳机需要）
$agent = Join-Path $dist 'tools\BleAgent'
if (Test-Path $agent) {
    $agentTarget = Join-Path $stage 'tools'
    New-Item -ItemType Directory -Force -Path $agentTarget | Out-Null
    Copy-Item (Join-Path $agent '*') $agentTarget -Recurse -Force
    Write-Host '==> 已带上 BLE 广播助手' -ForegroundColor DarkGray
} else {
    Write-Warning '未找到 dist\tools\BleAgent，苹果/小米耳机功能将不可用'
}

foreach ($name in @('install.ps1', 'uninstall.ps1', 'README.md')) {
    $src = Join-Path $dist $name
    if (Test-Path $src) { Copy-Item $src (Join-Path $stage $name) -Force }
}
$docs = Join-Path $dist 'docs'
if (Test-Path $docs) { Copy-Item $docs (Join-Path $stage 'docs') -Recurse -Force }

# 预览图（README 里引用的）
Get-ChildItem $dist -Filter 'preview-*.png' -File -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $stage $_.Name) -Force }

# ── 3. 压缩 ──────────────────────────────────────────────────────────────────
$zip = Join-Path $releaseRoot "BatteryBar-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$zipInfo = Get-Item $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$fileCount = (Get-ChildItem $stage -Recurse -File | Measure-Object).Count

Write-Host ''
Write-Host '==> 打包完成' -ForegroundColor Green
Write-Host ("    目录 : {0}  ({1} 个文件)" -f $stage, $fileCount)
Write-Host ("    压缩 : {0}" -f $zip)
Write-Host ("    大小 : {0:N1} MB" -f ($zipInfo.Length / 1MB))
Write-Host ("    SHA256: {0}" -f $hash)

# ── 4. 打 tag（可选）──────────────────────────────────────────────────────────
if ($Tag) {
    Push-Location $root
    try {
        $existing = git tag --list $tagName
        if ($existing) {
            Write-Warning "tag $tagName 已存在，跳过"
        } else {
            git add -A
            $dirty = git status --porcelain
            if ($dirty) {
                git commit -m "release: $tagName" | Out-Null
                Write-Host "==> 已提交发布改动" -ForegroundColor DarkGray
            }
            git tag -a $tagName -m "BatteryBar $Version"
            Write-Host "==> 已创建 tag $tagName（尚未推送）" -ForegroundColor Cyan
            Write-Host "    推送: git push origin $tagName" -ForegroundColor DarkGray
        }
    } finally { Pop-Location }
}

# ── 5. 上传 GitHub Release（可选，需要 gh CLI）───────────────────────────────
if ($Upload) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) {
        Write-Warning '未安装 gh CLI，无法自动上传。手动步骤：'
        Write-Host "  1) 打开 https://github.com/Frednko/BTDeviceBatteryView/releases/new?tag=$tagName"
        Write-Host "  2) 标题填 BatteryBar $Version，附件选择："
        Write-Host "     $zip"
        Write-Host '  3) 发布'
    } else {
        Push-Location $root
        try {
            gh release create $tagName $zip --title "BatteryBar $Version" --notes "BatteryBar $Version"
        } finally { Pop-Location }
    }
}
