param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Clicker
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@

[Clicker]::SetProcessDPIAware() | Out-Null
[Clicker]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 120
[Clicker]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[Clicker]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Write-Host "clicked $X,$Y"
