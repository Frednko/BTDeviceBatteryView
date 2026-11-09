# BatteryBar · 电池助手

Windows 托盘电量显示工具：自动读取无线鼠标、手柄、耳机等设备的电量，在任务栏显示，
低电量时提醒。单文件 exe，无运行库依赖，内存占用约 50 MB。

## 目录结构

```
app/                源码
  src/              C# 5（.NET Framework 4.8，WinForms + GDI+ 自绘）
    Hidpp.cs        罗技 HID++ 2.0 / 1.0 电量与设备名
    Gamepad.cs      PS5 DualSense / PS4 DualShock 报文解析
    Hid.cs          通用 HID Battery Strength
    BleAdv.cs       BLE 广播（苹果 / Fast Pair 耳机）
    Discovery.cs    设备发现、去重、排序
    Providers.cs    读取策略与通用回退链（BatteryResolver）
    Rules.cs        常见设备族预设表
    TrayApp.cs      托盘、轮询调度、通知、开机自启
    MainForm.cs     主界面（列表 + 详情）
    Theme.cs        配色、电量配色、闪电图标
    TrayIcon.cs     动态生成的多尺寸任务栏图标
    Dialogs.cs      添加设备 / 弹窗 / 通知 / 菜单
    Preview.cs      界面预览图生成
  tools/            构建辅助脚本
  refs/Facades/     编译 WinRT 需要的 facade 引用
  build.ps1         一键构建（csc 两遍：先导出图标，再嵌 exe）
build/bleagent/     BLE 助手（PyInstaller）打包工程
dist/               交付物（exe、文档、安装脚本、预览图）
preview/            界面截图
```

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File app\build.ps1
```

产物在 `dist\BatteryBar.exe`；若存在 `build\bleagent\dist2\BleAgent`，会一并复制到
`dist\tools\BleAgent`（苹果 / 小米耳机需要的 BLE 广播助手）。

## 发布打包

```powershell
powershell -ExecutionPolicy Bypass -File app\release.ps1            # 只出 zip
powershell -ExecutionPolicy Bypass -File app\release.ps1 -Tag       # 顺便打 tag
powershell -ExecutionPolicy Bypass -File app\release.ps1 -Tag -Upload   # 装了 gh CLI 可直接发 Release
powershell -ExecutionPolicy Bypass -File app\tools\create_release.ps1   # 没装 gh 时：用 GCM 里缓存的 GitHub 凭据调用 API 建 Release 并上传 zip
```

产物：`dist\release\BatteryBar-<版本>-win-x64\`（可直接拷走运行）与同名 `.zip`，
脚本会打印文件数、压缩包大小和 SHA256。版本号取自 `app\src\AssemblyInfo.cs`。

## 安装

```powershell
powershell -ExecutionPolicy Bypass -File dist\install.ps1
```

安装到 `%LOCALAPPDATA%\Programs\BatteryBar`，写 `HKCU\...\Run` 实现开机自启，可加桌面快捷方式。

## 支持的电量来源

| 设备 | 方式 |
| --- | --- |
| 通用 BLE | GATT `0x180F` / `0x2A19` |
| 罗技 | HID++ 2.0 `0x1004/0x1000/0x1001`，1.0 寄存器 `0x0D/0x07` |
| PS5 / PS4 手柄 | HID 输入报文（蓝牙 `0x31` / USB `0x01`） |
| Xbox 等 HID 设备 | HID `Battery Strength (0x06/0x20)` |
| AirPods / Beats | BLE 广播 Apple Continuity `0x004C/0x07`（部分型号加密，显示无法识别） |
| 小米 / Redmi 耳机 | BLE 广播 Fast Pair `0xFE2C` |
| 其它耳机 | 预设表提示所需协议（Sony SPP / HFP XAPL 暂未实现） |

详见 [dist/docs/device-presets.md](dist/docs/device-presets.md)。

## 说明

- 不使用手动输入电量：读不到就明确显示「无法识别」。
- 检测到罗技 G HUB / Logi Options+ 时自动进入低干扰模式（活跃时每 15 秒探测、空闲按刷新间隔），
  避免两个客户端争抢 HID++ 通道导致官方软件读到错值。
- 设备休眠时保留上次读数，唤醒后自动重读；连续 3 次无应答才标记休眠。
