using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryBar
{
    internal class Reading
    {
        public bool Ok;
        public bool Connected;
        public int Level = -1;
        public string Message = "";
        /// <summary>设备只是休眠 / 暂时无应答，应保留上次读数而不是清空。</summary>
        public bool Sleeping;
        /// <summary>正在充电（用于显示闪电标志）。</summary>
        public bool Charging;

        public static Reading Fail(string message, bool connected)
        {
            Reading r = new Reading();
            r.Ok = false;
            r.Connected = connected;
            r.Message = message;
            return r;
        }

        public static Reading Sleep(string message)
        {
            Reading r = new Reading();
            r.Ok = false;
            r.Connected = false;
            r.Sleeping = true;
            r.Message = message;
            return r;
        }

        public static Reading Good(int level, string message)
        {
            return Good(level, message, false);
        }

        public static Reading Good(int level, string message, bool charging)
        {
            Reading r = new Reading();
            r.Ok = true;
            r.Connected = true;
            r.Level = level;
            r.Message = message;
            r.Charging = charging;
            return r;
        }
    }

    internal interface IProvider
    {
        Reading Read(DeviceConfig device);
    }

    internal static class ProviderFactory
    {
        public static IProvider Create(string source)
        {
            switch (source)
            {
                case Sources.Ble: return new BleGattProvider();
                case Sources.Hidpp: return new HidppProvider();
                case Sources.Gamepad: return new GamepadProvider();
                case Sources.Hid: return new HidBatteryProvider();
                case Sources.Adv: return new BleAdvProvider();
                default: return null;
            }
        }
    }

    /// <summary>
    /// 通用电量识别：按地址形态推断可用方式，逐个尝试，成功即记住；
    /// 都失败则标记「无法识别」，不再要求手动填写。
    /// </summary>
    internal static class BatteryResolver
    {
        public static Reading Read(DeviceConfig device)
        {
            List<string> candidates = Candidates(device);

            // 发现阶段就没判定出任何可用方式 —— 这才是真正的「无法识别」
            if (candidates.Count == 0)
            {
                device.UnknownSource = true;
                return Reading.Fail("该设备不上报电量", false);
            }

            if (!string.IsNullOrEmpty(device.Source) && candidates.Contains(device.Source))
            {
                candidates.Remove(device.Source);
                candidates.Insert(0, device.Source);
            }

            Reading last = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                IProvider provider = ProviderFactory.Create(candidates[i]);
                if (provider == null) continue;
                Reading reading;
                try
                {
                    reading = provider.Read(device);
                }
                catch (Exception ex)
                {
                    reading = Reading.Fail("读取异常：" + ex.Message, false);
                }
                if (reading.Ok && reading.Level >= 0)
                {
                    device.Source = candidates[i];
                    device.UnknownSource = false;
                    return reading;
                }
                last = reading;
            }

            // 有可用方式但这次没读到（休眠 / 接收器暂时不可用）：不算「无法识别」，
            // 保留上次读数，交给调度器快速重试
            device.UnknownSource = false;
            if (last != null) return last;
            return Reading.Fail("读取失败", false);
        }

        /// <summary>根据发现阶段判定的来源 + 地址形态，列出候选读取方式。</summary>
        public static List<string> Candidates(DeviceConfig device)
        {
            List<string> list = new List<string>();
            if (device == null) return list;
            string address = device.Address;

            if (device.Source == Sources.Adv)
            {
                list.Add(Sources.Adv);
                return list;
            }
            if (device.Source == Sources.Hid)
            {
                list.Add(Sources.Hid);
                return list;
            }
            if (device.Source == Sources.Gamepad)
            {
                list.Add(Sources.Gamepad);
                list.Add(Sources.Hid);
                return list;
            }
            if (device.Source == Sources.Hidpp || (!string.IsNullOrEmpty(address) && address.IndexOf('#') > 0))
            {
                list.Add(Sources.Hidpp);
                return list;
            }
            if (device.Source == Sources.Ble)
            {
                list.Add(Sources.Ble);
                return list;
            }
            // 发现阶段没有判定出可用的读取方式：保持「无法识别」，不再猜测
            return list;
        }
    }

    // ------------------------------------------------------------------
    //  HID 枚举工具
    // ------------------------------------------------------------------
    internal class HidDeviceInfo
    {
        public string Path;
        public ushort VendorId;
        public ushort ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public int InputLength;
        public int OutputLength;
        public string Product;
    }

    internal static class HidDevices
    {
        public static List<HidDeviceInfo> Enumerate()
        {
            List<HidDeviceInfo> list = new List<HidDeviceInfo>();
            Guid hidGuid;
            Native.HidD_GetHidGuid(out hidGuid);
            foreach (string path in Native.EnumDeviceInterfaces(hidGuid))
            {
                using (SafeFileHandle h = Native.OpenDevice(path, false))
                {
                    if (h.IsInvalid) continue;
                    Native.HIDD_ATTRIBUTES attrs = new Native.HIDD_ATTRIBUTES();
                    attrs.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                    if (!Native.HidD_GetAttributes(h, ref attrs)) continue;
                    HidDeviceInfo info = new HidDeviceInfo();
                    info.Path = path;
                    info.VendorId = attrs.VendorID;
                    info.ProductId = attrs.ProductID;
                    Native.HIDP_CAPS caps = new Native.HIDP_CAPS();
                    caps.Reserved = new ushort[17];
                    IntPtr pp;
                    if (Native.HidD_GetPreparsedData(h, out pp))
                    {
                        Native.HidP_GetCaps(pp, ref caps);
                        Native.HidD_FreePreparsedData(pp);
                    }
                    info.UsagePage = caps.UsagePage;
                    info.Usage = caps.Usage;
                    info.InputLength = caps.InputReportByteLength;
                    info.OutputLength = caps.OutputReportByteLength;
                    StringBuilder sb = new StringBuilder(260);
                    if (Native.HidD_GetProductString(h, sb, 520)) info.Product = sb.ToString();
                    list.Add(info);
                }
            }
            return list;
        }

        public static string VidPid(HidDeviceInfo d)
        {
            return d.VendorId.ToString("X4") + ":" + d.ProductId.ToString("X4");
        }
    }

    // ------------------------------------------------------------------
    //  罗技 HID++（2.0 FAP + 1.0 RAP）
    // ------------------------------------------------------------------
    internal class HidppProvider : IProvider
    {
        private static readonly object Gate = new object();

        public Reading Read(DeviceConfig device)
        {
            lock (Gate)
            {
                string vidPid;
                int index;
                if (!Hidpp.ParseAddress(device.Address, out vidPid, out index))
                    return Reading.Fail("设备地址无效，请重新添加", false);

                HidppTarget target = null;
                List<HidppTarget> targets = Hidpp.FindTargets();
                for (int i = 0; i < targets.Count; i++)
                {
                    if (string.Equals(targets[i].VidPid, vidPid, StringComparison.OrdinalIgnoreCase)) { target = targets[i]; break; }
                }

                if (target != null)
                {
                    // 无线鼠标会休眠，连发两次请求把它叫醒
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        HidppBattery battery = TryRead(target, index);
                        if (battery != null && battery.Level >= 0) return ToReading(battery);
                        if (attempt == 0) Thread.Sleep(120);
                    }
                }

                // 换了接收器 / 换了 USB 接口（接收器 PID 变化）：先在其它接收器上用同一 index 试一次
                if (target == null)
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        HidppBattery sameIndex = TryRead(targets[i], index);
                        if (sameIndex != null && sameIndex.Level >= 0) return ToReading(sameIndex);
                    }
                }

                // 仍然不行就按设备名在其它接收器上重新定位
                if (!string.IsNullOrEmpty(device.Name))
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (targets[i] == target) continue;
                        HidppBattery battery = TryReadByName(targets[i], device.Name);
                        if (battery != null && battery.Level >= 0) return ToReading(battery);
                    }
                }

                // 休眠 / 接收器暂时不可用都属于「稍后会恢复」，交给快速重试处理
                return Reading.Sleep("接收器未连接或设备休眠 · 动一下鼠标即可刷新");
            }
        }

        /// <summary>在接收器上按设备名查找并读取电量。</summary>
        private static HidppBattery TryReadByName(HidppTarget target, string expectedName)
        {
            HidDeviceInfo iface = target.LongInterface ?? target.ShortInterface;
            if (iface == null) return null;
            using (HidppSession session = new HidppSession(iface, true))
            {
                if (!session.IsOpen) return null;
                for (byte index = 1; index <= 6; index++)
                {
                    string name = Hidpp.DeviceName(session, index);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)) continue;
                    return Hidpp.ReadBattery(session, index);
                }
            }
            return null;
        }

        private static HidppBattery TryRead(HidppTarget target, int index)
        {
            HidppBattery battery = null;
            if (target.LongInterface != null)
            {
                using (HidppSession session = new HidppSession(target.LongInterface, true))
                {
                    if (session.IsOpen)
                    {
                        battery = Hidpp.ReadBattery(session, (byte)index);
                        if (battery.Level >= 0) return battery;
                    }
                }
            }
            if (target.ShortInterface != null)
            {
                using (HidppSession session = new HidppSession(target.ShortInterface, true))
                {
                    if (session.IsOpen)
                    {
                        battery = Hidpp.ReadBattery(session, (byte)index);
                        if (battery.Level >= 0) return battery;
                    }
                }
            }
            return battery;
        }

        private static Reading ToReading(HidppBattery battery)
        {
            string message = "罗技 HID++";
            if (!string.IsNullOrEmpty(battery.Detail)) message += " · " + battery.Detail;
            Reading reading = Reading.Good(battery.Level, message, battery.Charging);
            reading.Connected = battery.Connected;
            return reading;
        }
    }

    // ------------------------------------------------------------------
    //  蓝牙低功耗 GATT 电池服务
    // ------------------------------------------------------------------
    internal class BleGattProvider : IProvider
    {
        public Reading Read(DeviceConfig device)
        {
            string address = Normalize(device.Address);
            List<string> interfaces;
            try { interfaces = Native.EnumDeviceInterfaces(Native.BleDeviceInterface); }
            catch (Exception ex) { return Reading.Fail("蓝牙接口枚举失败：" + ex.Message, false); }

            string path = null;
            for (int i = 0; i < interfaces.Count; i++)
            {
                string instanceId = Native.GetInterfaceStringProperty(interfaces[i], Native.PKEY_Device_InstanceId);
                string probe = Normalize((instanceId ?? "") + interfaces[i]);
                if (address.Length > 0 && probe.IndexOf(address, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    path = interfaces[i];
                    break;
                }
            }
            if (path == null) return Reading.Sleep("设备不在范围内 · 请先连接");

            using (SafeFileHandle h = Native.OpenDevice(path, true))
            {
                if (h.IsInvalid) return Reading.Fail("无法打开设备(" + Marshal.GetLastWin32Error() + ")", false);
                IntPtr handle = h.DangerousGetHandle();
                try
                {
                    ushort count = 0;
                    int hr = Native.BluetoothGATTGetServices(handle, ref count, IntPtr.Zero, Native.BLUETOOTH_GATT_FLAG_NONE);
                    if (count == 0) return Reading.Fail("设备未连接", false);

                    int serviceSize = Marshal.SizeOf(typeof(Native.BTH_LE_GATT_SERVICE));
                    IntPtr buffer = Marshal.AllocHGlobal(serviceSize * count);
                    try
                    {
                        hr = Native.BluetoothGATTGetServices(handle, ref count, buffer, Native.BLUETOOTH_GATT_FLAG_NONE);
                        if (hr < 0) return Reading.Fail("读取服务失败(0x" + ((uint)hr).ToString("X8") + ")", false);

                        for (int i = 0; i < count; i++)
                        {
                            Native.BTH_LE_GATT_SERVICE svc = (Native.BTH_LE_GATT_SERVICE)Marshal.PtrToStructure(
                                new IntPtr(buffer.ToInt64() + i * serviceSize), typeof(Native.BTH_LE_GATT_SERVICE));
                            if (!IsBatteryService(svc.ServiceUuid)) continue;

                            ushort charCount = 0;
                            hr = Native.BluetoothGATTGetCharacteristics(handle, ref svc, ref charCount, IntPtr.Zero, Native.BLUETOOTH_GATT_FLAG_NONE);
                            if (charCount == 0) continue;

                            int charSize = Marshal.SizeOf(typeof(Native.BTH_LE_GATT_CHARACTERISTIC));
                            IntPtr charBuffer = Marshal.AllocHGlobal(charSize * charCount);
                            try
                            {
                                hr = Native.BluetoothGATTGetCharacteristics(handle, ref svc, ref charCount, charBuffer, Native.BLUETOOTH_GATT_FLAG_NONE);
                                if (hr < 0) continue;

                                for (int c = 0; c < charCount; c++)
                                {
                                    Native.BTH_LE_GATT_CHARACTERISTIC ch = (Native.BTH_LE_GATT_CHARACTERISTIC)Marshal.PtrToStructure(
                                        new IntPtr(charBuffer.ToInt64() + c * charSize), typeof(Native.BTH_LE_GATT_CHARACTERISTIC));
                                    if (!IsBatteryLevel(ch.CharacteristicUuid)) continue;

                                    uint needed = 0;
                                    Native.BluetoothGATTGetCharacteristicValue(handle, ref ch, 0, IntPtr.Zero, out needed, Native.BLUETOOTH_GATT_FLAG_NONE);
                                    if (needed == 0) needed = 16;
                                    IntPtr valueBuffer = Marshal.AllocHGlobal((int)needed + 8);
                                    try
                                    {
                                        int vhr = Native.BluetoothGATTGetCharacteristicValue(handle, ref ch, needed, valueBuffer, out needed, Native.BLUETOOTH_GATT_FLAG_NONE);
                                        if (vhr < 0) return Reading.Fail("读取电量失败(0x" + ((uint)vhr).ToString("X8") + ")", true);
                                        uint size = (uint)Marshal.ReadInt32(valueBuffer);
                                        if (size < 1) return Reading.Fail("电量数据为空", true);
                                        int level = Marshal.ReadByte(valueBuffer, 4);
                                        if (level < 0 || level > 100) return Reading.Fail("电量数据异常", true);
                                        return Reading.Good(level, "蓝牙 GATT");
                                    }
                                    finally { Marshal.FreeHGlobal(valueBuffer); }
                                }
                            }
                            finally { Marshal.FreeHGlobal(charBuffer); }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                catch (Exception ex)
                {
                    return Reading.Fail("GATT 异常：" + ex.Message, false);
                }
            }
            return Reading.Fail("该设备未提供标准电量服务", true);
        }

        private static bool IsBatteryService(Native.BTH_LE_UUID uuid)
        {
            if (uuid.IsShortUuid != 0) return uuid.ShortUuid == 0x180F;
            return uuid.LongUuid == Native.BatteryServiceUuid;
        }

        private static bool IsBatteryLevel(Native.BTH_LE_UUID uuid)
        {
            if (uuid.IsShortUuid != 0) return uuid.ShortUuid == 0x2A19;
            return uuid.LongUuid == Native.BatteryLevelCharUuid;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == ':' || ch == '-' || ch == '&' || ch == '\\' || ch == '?' || ch == '_' || ch == '#') continue;
                sb.Append(ch);
            }
            return sb.ToString().ToUpperInvariant();
        }
    }
}
