<#
    BatteryBar build script
    Uses the built-in .NET Framework 4.8 compiler (no SDK required).
    Two passes: build -> dump app.ico -> rebuild with embedded icon.
#>
param(
    [switch]$Run,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path (Split-Path -Parent $root) 'dist'
$obj = Join-Path $root 'obj'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
if ($Clean) {
    # only clean build artifacts; keep dist\README.md, install.ps1, preview images
    Remove-Item $obj -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $dist 'BatteryBar.exe') -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $dist, $obj | Out-Null

$refs = @()
foreach ($name in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll','System.Runtime.Serialization.dll','System.Xml.dll','System.Web.Extensions.dll')) {
    $refs += ('/r:' + (Join-Path $fw $name))
}

$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })
$manifest = Join-Path $root 'app.manifest'
$ico = Join-Path $obj 'app.ico'
$exe = Join-Path $dist 'BatteryBar.exe'

function Invoke-Csc {
    param([string[]]$extra)
    $argv = @('/nologo','/target:winexe','/platform:x64','/optimize+','/langversion:5','/nowarn:1591','/codepage:65001')
    $argv += ('/win32manifest:"' + $manifest + '"')
    $argv += ('/out:"' + $exe + '"')
    $argv += $refs
    if ($extra) { $argv += $extra }
    $argv += $sources
    & $csc $argv
    if ($LASTEXITCODE -ne 0) { throw "compile failed (exit $LASTEXITCODE)" }
}

Write-Host '==> pass 1: build' -ForegroundColor Cyan
Invoke-Csc

Write-Host '==> export app icon' -ForegroundColor Cyan
$iconArg = '--icon=' + $ico
& $exe $iconArg | Out-Null

Write-Host '==> pass 2: build with icon' -ForegroundColor Cyan
Invoke-Csc @('/win32icon:"' + $ico + '"')

# 把 BLE 广播助手一起放进 dist\tools
$agentSrc = Join-Path (Split-Path -Parent $root) 'build\bleagent\dist2\BleAgent'
if (Test-Path $agentSrc) {
    $agentDst = Join-Path $dist 'tools\BleAgent'
    Remove-Item $agentDst -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $agentDst | Out-Null
    Copy-Item (Join-Path $agentSrc '*') $agentDst -Recurse -Force
    Write-Host '==> bundled BleAgent.exe (BLE advertisement helper)' -ForegroundColor Cyan
}

$info = Get-Item $exe
Write-Host ('==> done: {0} ({1:N0} KB)' -f $info.FullName, ($info.Length / 1KB)) -ForegroundColor Green

if ($Run) { Start-Process -FilePath $exe }
