# 常见蓝牙设备电量识别预设（研究 + 规则表）

> 这份表是 `app/src/Rules.cs` 里 `DeviceRules` 的数据来源，也是后续扩展的路线图。
> 界面上的「无法识别 · Sony 耳机 · 需要 Sony SPP …」提示就来自这张表。

## 一、结论速览

| 设备族 | 电量到底在哪 | 现状 |
| --- | --- | --- |
| 通用 BLE 鼠标/键盘/手环/耳塞 | GATT 电池服务 `0x180F` / 特性 `0x2A19` | ✅ 已实现 |
| 罗技（Lightspeed/Unifying/Bolt 接收器） | HID++ 2.0 `0x1004 / 0x1000 / 0x1001`，老设备用 1.0 寄存器 `0x0D/0x07` | ✅ 已实现 |
| PS5 DualSense / Edge、PS4 DualShock 4 | 主输入报文：蓝牙 `0x31` 第 54 字节、USB `0x01` 第 53 字节（低 4 位档位、高 4 位充电状态） | ✅ 已实现 |
| Xbox 无线手柄 | HID 标准用法 `Generic Device Controls(0x06) / Battery Strength(0x20)` | ✅ 走通用 HID 通道 |
| Sony 耳机（WH/WF/1000X 系列） | Sony 私有 SPP（RFCOMM 31 字节帧）或 HFP 的 `AT+XAPL` 电池扩展 | ⛔ 需新增 RFCOMM 传输 |
| Apple AirPods / Beats | 不在 GATT，而在 BLE **广播**里的 Apple Continuity 报文（厂商标识 `0x004C`，类型 `0x07`），含左右耳/充电盒电量 | ⛔ 需 BLE 广播扫描 |
| 小米 / Redmi 耳机 | BLE 广播（Fast Pair `0xFE2C` 服务数据）或小米私有 GATT | ✅ Fast Pair 广播已支持 |
| JBL / Bose / Jabra / 森海 / 漫步者 | 多数支持 HFP `AT+XAPL` + `AT+IPHONEACCEV` | ⛔ 需 HFP 传输 |
| 经典蓝牙耳机（无电池扩展） | 无标准接口 | 如实显示「无法识别」 |

## 二、各协议要点

### 1) 通用 BLE：GATT 电池服务（已实现）

- 服务 `0000180F-0000-1000-8000-00805F9B34FB`，特性 `00002A19-…`，读 1 字节 = 0–100。
- Windows 侧用 `BluetoothGATTGetServices/GetCharacteristics/GetCharacteristicValue`（`bluetoothapis.dll`），不需要 WinRT。

### 2) 罗技 HID++（已实现）

- 2.0：`Root.GetFeature(0x1004)` 拿特性索引 → `GetStatus(func 1)` 的 `params[0]` 就是电量百分比，`params[2]` 是充电状态。
- 老设备回落到 `0x1000`（18 字节容量）与 `0x1001`（电压 mV），再不行用 1.0 寄存器 `0x0D`（百分比）/`0x07`（四档）。
- 设备名来自特性 `0x0005`（参数是**字节偏移**，不是分块号——这点在 SUPERLIGHT 2 SE 上实测确认）。

### 3) PlayStation 手柄（已实现）

- 报文结构（Linux `hid-playstation.c`）：主输入报文尾部有 `status[3]`，`status[0]` 低 4 位 = 电量档位（0–9 表示 0–90%，10 = 100%），高 4 位 = 充电状态（0 放电 / 1 充电 / 2 充满 / 0xA,0xB 异常 / 0xF 错误）。
- 蓝牙报文比 USB 多 2 字节头，所以同一字段在蓝牙里是第 54 字节、USB 里是第 53 字节。

### 4) Sony 耳机：Sony SPP 或 HFP 电池扩展（待实现）

- 索尼自家 App 走 **SPP（RFCOMM）**，帧长 31 字节（`SonyHeadphonesClient` 逆向的私有命令集），可取电量、降噪模式等。
- 另一条路是 **HFP 的 Apple 电池扩展**：连上 HFP 后由 AG 发 `AT+XAPL=<vendor>-<product>-<ver>,<features>`，耳机回 `+XAPL=`，随后主动上报
  `+IPHONEACCEV=<n>,<key>,<val>,…`，其中 `key=1` 是电量（0–9 档）、`key=2` 是充电状态。
- 两者都能用 Win32 的 **Winsock AF_BTH（BTHPROTO_RFCOMM）** 直接连，不依赖 WinRT。

### 5) Apple AirPods / Beats（已支持：BleAgent 扫描广播）

- 电量不在 GATT 里，而在 BLE **广播数据**：Manufacturer Specific Data，公司 ID `0x004C`（Apple），Continuity 消息类型 `0x07`（Proximity Pairing）。
  解析后可得到左/右耳/充电盒电量与充电标志。
- Windows 上抓 BLE 广播需要 `BluetoothLEAdvertisementWatcher`（WinRT）；.NET Framework 下的替代方案是起一个
  PowerShell/WinRT 辅助进程（或改用 WinRT 投影）。

### 6) 小米 / Redmi 耳机（Fast Pair 广播已支持）

- 常见两条来源：Google **Fast Pair**（服务数据 `0xFE2C`，Model ID + 电池字节）与小米私有 GATT 服务。
- 与 AirPods 一样需要 BLE 广播扫描能力。

### 7) Xbox 手柄

- 走 HID 标准 `Battery Strength` 用法，属于「通用 HID 电量」通道；只要设备在报告描述符里声明了该用法即可读取。

## 三、规则表（已写入 `Rules.cs`）

| 匹配关键字（设备名包含） | 归类 | 处理方式 |
| --- | --- | --- |
| logitech / 罗技 / usb receiver / lightspeed | 罗技无线设备 | HID++ |
| dualsense / dualshock / wireless controller / ps5 / ps4 | PlayStation 手柄 | 手柄 HID 报文 |
| xbox | Xbox 手柄 | HID Battery Strength |
| sony / wh- / wf- / wi- / xm4 / xm5 / 1000x / linkbuds | Sony 耳机 | 需 SPP/HFP（提示用户） |
| airpods / beats / powerbeats / apple | Apple 耳机 | 需 BLE 广播（提示用户） |
| xiaomi / redmi / 小米 / 红米 / mi buds / airdots | 小米耳机 | 需 BLE 广播（提示用户） |
| jbl / bose / jabra / sennheiser / soundcore / anker / edifier / 漫步者 | HFP 电池扩展耳机 | 需 HFP XAPL（提示用户） |

命中后界面会显示该设备「需要什么协议、当前是否支持」，而不是笼统的「无法识别」。

## 四、下一步实现顺序（按性价比）

1. **HFP `AT+XAPL`（RFCOMM）** —— 一次实现覆盖 Sony/JBL/Bose/Jabra/漫步者 等大量耳机；纯 Win32，无需 WinRT。
2. **BLE 广播解析（AirPods / Fast Pair）** —— 需要 WinRT，建议做成独立的小辅助进程，通过命名管道/临时文件回传电量。
3. **Sony SPP** —— 覆盖更全（降噪状态等），但命令集更复杂，放在 HFP 之后。

## 参考资料

- Linux `hid-playstation.c`（DualSense/DS4 报文与电量编码）：<https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git/plain/drivers/hid/hid-playstation.c>
- Linux `hid-logitech-hidpp.c`（HID++ 2.0/1.0 电量）：<https://sources.debian.org/data/main/l/linux/5.10.46-4/drivers/hid/hid-logitech-hidpp.c>
- Apple Continuity 广播解析（AirPods 电量）：<https://github.com/bhemsen/podbridge/blob/ae1efd46f0cbbb9da38aaa4815feb543a63778c0/docs/research/continuity-parser.md>
- Sony SPP 私有协议实现参考：<https://github.com/yuxiximyxx/magicxm4>
- HFP 取耳机电量（AT+XAPL / AT+IPHONEACCEV）：<https://stackoverflow.com/questions/62932249/use-bluetooth-hfp-profile-to-get-battery-level-from-headset>
- Apple 配件设计指南（XAPL 电池扩展，存档版）：<https://web.archive.org/web/20240108061855/https://developer.apple.com/accessories/Accessory-Design-Guidelines.pdf>

## 五、本机实测记录（2025-11）

用 Winsock `AF_BTH` 直接连两台已连接耳机的服务通道，结果如下：

| 目标 | 服务 | 结果 |
| --- | --- | --- |
| WH-CH700N | HFP-HS `0x111E`（RFCOMM） | `connect` 失败 `10064`（WSAEHOSTDOWN） |
| 另一台蓝牙耳机 | HFP-HS `0x111E`（RFCOMM） | `connect` 失败 `10048`（WSAEADDRINUSE，**被 Windows 音频栈占用**） |
| 两台耳机 | AAP L2CAP PSM `0x1001`（Apple 私有） | `socket` 失败 `10044`（WSAEPROTONOSUPPORT，**Windows 不支持 L2CAP 套接字**） |

结论：**HFP 通道被系统音频占用、Apple AAP 走 L2CAP 而 Windows 不支持**，所以这两条路在当前技术栈里走不通；
AirPods 的电量只能走 BLE 广播。

BLE 广播用 Python + bleak 实测可以抓到（示例为苹果设备的 Proximity Pairing 广播）：

```
MFG 004C: 07 19 01 0E 20 75 AA B8 31 00 04 FC 24 33 87 …
MFG 004C: 07 19 01 0E 20 2B 99 8F 01 00 05 71 A6 98 …
```

首字节 `0x07` 即 Proximity Pairing，`0E 20` 是型号，后面的字节含左右耳/充电盒电量与充电位。
下一步只需把「BLE 广播助手」接上（可选 Python/bleak 或 WinRT 辅助进程），按 0x07 布局解析即可，
无需改动现有主程序架构。