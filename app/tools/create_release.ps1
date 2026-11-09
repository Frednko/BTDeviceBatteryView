$ErrorActionPreference = 'Stop'
$root = 'C:\Users\23524\Documents\BatteryBar'
$repo = 'Frednko/BTDeviceBatteryView'
$tag = 'v1.0.0'
$zip = Join-Path $root 'dist\release\BatteryBar-1.0.0-win-x64.zip'

if (-not (Test-Path $zip)) { throw "找不到 $zip" }

# 从 Git Credential Manager 取 GitHub 凭据（不打印明文）
$env:GIT_TERMINAL_PROMPT = '0'
$input = "protocol=https`nhost=github.com`n`n"
$out = $input | git -C $root credential fill 2>&1
$token = ($out | Select-String -Pattern '^password=(.+)$').Matches.Groups[1].Value
$user = ($out | Select-String -Pattern '^username=(.+)$').Matches.Groups[1].Value
if (-not $token) { throw "未取到 GitHub 令牌（GCM 里没有缓存）" }
Write-Host ("凭据: {0}, token 长度 {1}" -f $user, $token.Length)

$headers = @{
    Authorization          = "token $token"
    Accept                 = 'application/vnd.github+json'
    'User-Agent'           = 'BatteryBar-Release'
    'X-GitHub-Api-Version' = '2022-11-28'
}

# 1) 查询是否已有该 tag 的 release
$existing = $null
try {
    $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $headers -Method Get
    Write-Host "已存在 release: $($existing.html_url)"
} catch { Write-Host "尚未创建 release，开始创建…" }

if (-not $existing) {
    $body = @{
        tag_name   = $tag
        name       = 'BatteryBar 1.0.0'
        body       = "Windows 托盘电量显示工具。`n`n- 自动读取罗技 / PS 手柄 / 通用 HID / BLE 广播设备电量`n- 任务栏图标显示电量，低电量提醒`n- 解压后运行 install.ps1 安装，或直接运行 BatteryBar.exe`n`n详见 README.md 与 docs\device-patterns.md"
        draft      = $false
        prerelease = $false
    } | ConvertTo-Json
    $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases" -Headers $headers -Method Post -Body $body -ContentType 'application/json'
    Write-Host "已创建 release: $($existing.html_url)"
}

# 2) 上传附件
$name = Split-Path $zip -Leaf
$uploadUrl = "https://uploads.github.com/repos/$repo/releases/$($existing.id)/assets?name=$name"
$asset = Invoke-RestMethod -Uri $uploadUrl -Headers $headers -Method Post -InFile $zip -ContentType 'application/zip'
Write-Host "已上传附件: $($asset.name) ($([math]::Round($asset.size/1MB,1)) MB)"
Write-Host "下载地址: $($asset.browser_download_url)"
Write-Host "Release 页面: $($existing.html_url)"
