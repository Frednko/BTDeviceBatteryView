using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BatteryBar
{
    internal class DiscoveredDevice
    {
        public string Id;
        public string Name;
        public string Source;
        public string Address;
        public string Detail;
        public bool Added;
    }

    /// <summary>扫描系统中已配对的蓝牙设备与可自动读取的无线设备。</summary>
    internal static class DeviceScanner
    {
        private const string BthPortKey = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices";

        public static List<DiscoveredDevice> Scan()
        {
            Dictionary<string, DiscoveredDevice> map = new Dictionary<string, DiscoveredDevice>();
            Dictionary<string, string> btNames = new Dictionary<string, string>();
            Dictionary<string, DateTime> btLastSeen = new Dictionary<string, DateTime>();

            // 1) 注册表中的已配对蓝牙设备
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(BthPortKey))
                {
                    if (root != null)
                    {
                        foreach (string sub in root.GetSubKeyNames())
                        {
                            using (RegistryKey key = root.OpenSubKey(sub))
                            {
                                if (key == null) continue;
                                string address = NormalizeAddress(sub);
                                if (address.Length != 12) continue;
                                string name = ReadString(key, "FriendlyName");
                                if (string.IsNullOrEmpty(name)) name = DecodeName(key.GetValue("Name") as byte[]);
                                if (string.IsNullOrEmpty(name)) continue;
                                DateTime last = ReadFileTime(key, "LastConnected");
                                if (last == DateTime.MinValue) last = ReadFileTime(key, "LastSeen");
                                btNames[address] = name;
                                btLastSeen[address] = last;
                            }
                        }
                    }
                }
            }
            catch { }

            // 2) 蓝牙低功耗接口（可自动读取电量）
            try
            {
                foreach (string path in Native.EnumDeviceInterfaces(Native.BleDeviceInterface))
                {
                    string instanceId = Native.GetInterfaceStringProperty(path, Native.PKEY_Device_InstanceId) ?? "";
                    string address = ExtractAddress(instanceId);
                    if (address.Length != 12) address = ExtractAddress(path);
                    if (address.Length != 12) continue;

                    string name = Native.GetInterfaceStringProperty(path, Native.PKEY_Device_FriendlyName);
                    string btName;
                    if (btNames.TryGetValue(address, out btName) && !string.IsNullOrEmpty(btName)) name = btName;
                    if (string.IsNullOrEmpty(name) || name == "蓝牙设备") name = "蓝牙设备 " + address.Substring(8);

                    DiscoveredDevice d = new DiscoveredDevice();
                    d.Id = "bt:" + address;
                    d.Name = name;
                    d.Source = Sources.Ble;
                    d.Address = address;
                    DateTime seen;
                    d.Detail = "蓝牙低功耗 · 可自动读取电量";
                    if (btLastSeen.TryGetValue(address, out seen)) d.Detail += " · 上次连接 " + Human(seen);
                    map[d.Id] = d;
                }
            }
            catch { }
            // 3) PlayStation 手柄（DualSense / DualShock 4，输入报文里带电量）
            HashSet<string> hiddenBt = new HashSet<string>();
            try
            {
                List<HidDeviceInfo> allHid = HidDevices.Enumerate();
                Dictionary<string, bool> seenGamepads = new Dictionary<string, bool>();
                for (int i = 0; i < allHid.Count; i++)
                {
                    HidDeviceInfo info = allHid[i];
                    if (!GamepadBattery.IsSupported(info.VendorId, info.ProductId)) continue;
                    if (info.UsagePage != 0x0001 || info.Usage != 0x0005) continue;
                    string vidPid = HidDevices.VidPid(info);
                    if (seenGamepads.ContainsKey(vidPid)) continue;
                    seenGamepads[vidPid] = true;

                    DiscoveredDevice d = new DiscoveredDevice();
                    d.Id = "pad:" + vidPid;
                    d.Name = string.IsNullOrEmpty(info.Product) ? "PlayStation 手柄" : info.Product;
                    d.Source = Sources.Gamepad;
                    d.Address = vidPid;
                    d.Detail = "手柄 HID 报文 · 可自动读取电量";
                    map[d.Id] = d;

                    // 同名蓝牙条目（经典蓝牙）不再重复列出
                    foreach (KeyValuePair<string, string> pair in btNames)
                    {
                        if (string.Equals(pair.Value, d.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            hiddenBt.Add(pair.Key);
                            map.Remove("bt:" + pair.Key);
                        }
                    }
                }
            }
            catch { }

            // 3b) 其它 HID 设备：尝试标准的 HID 电量用法（任何型号都适用）
            try
            {
                List<HidDeviceInfo> hidList = HidDevices.Enumerate();
                Dictionary<string, bool> seenHid = new Dictionary<string, bool>();
                int budget = Environment.TickCount + 2500;
                for (int i = 0; i < hidList.Count; i++)
                {
                    if (Environment.TickCount > budget) break;
                    HidDeviceInfo info = hidList[i];
                    if (info.VendorId == 0x046D) continue; // 罗技走 HID++ 通道
                    if (GamepadBattery.IsSupported(info.VendorId, info.ProductId)) continue;
                    if (info.UsagePage != 0x0001 && info.UsagePage != 0x000C) continue;
                    string vidPid = HidDevices.VidPid(info);
                    if (seenHid.ContainsKey(vidPid)) continue;
                    seenHid[vidPid] = true;

                    bool charging;
                    int level = HidBattery.ReadLevel(info, out charging);
                    if (level < 0) continue;

                    DiscoveredDevice d = new DiscoveredDevice();
                    d.Id = "hid:" + vidPid;
                    d.Name = string.IsNullOrEmpty(info.Product) ? ("HID 设备 " + vidPid) : info.Product;
                    d.Source = Sources.Hid;
                    d.Address = vidPid;
                    d.Detail = "HID 电量 · 可自动读取电量";
                    map[d.Id] = d;
                }
            }
            catch { }

            // 3c) BLE 广播设备（苹果耳机 / Fast Pair 耳机）
            try
            {
                if (BleAdvAgent.Available)
                {
                    List<AdvDevice> advs = BleAdvAgent.Get(true);
                    for (int i = 0; i < advs.Count; i++)
                    {
                        AdvDevice adv = advs[i];
                        string id = "adv:" + adv.Address;
                        if (map.ContainsKey(id)) continue;
                        DiscoveredDevice d = new DiscoveredDevice();
                        d.Id = id;
                        d.Name = string.IsNullOrEmpty(adv.Name)
                            ? ("BLE 耳机 " + adv.Address.Substring(adv.Address.Length - 5))
                            : adv.Name;
                        d.Source = Sources.Adv;
                        d.Address = adv.Address;
                        d.Detail = adv.Describe() + " · BLE 广播";
                        map[id] = d;
                    }
                }
            }
            catch { }

            // 4) 其余已配对的经典蓝牙设备
            foreach (KeyValuePair<string, string> pair in btNames)
            {
                string id = "bt:" + pair.Key;
                if (map.ContainsKey(id) || hiddenBt.Contains(pair.Key)) continue;
                DiscoveredDevice d = new DiscoveredDevice();
                d.Id = id;
                d.Name = pair.Value;
                d.Source = "";
                d.Address = pair.Key;
                DateTime seen;
                d.Detail = "经典蓝牙 · " + DeviceRules.Hint(pair.Value);
                if (btLastSeen.TryGetValue(pair.Key, out seen) && seen > DateTime.MinValue) d.Detail += " · 上次连接 " + Human(seen);
                map[id] = d;
            }



            // 5) 罗技 HID++ 设备（一个接收器下可能有多台配对设备）
            try
            {
                List<HidppTarget> targets = Hidpp.FindTargets();
                for (int t = 0; t < targets.Count; t++)
                {
                    HidppTarget target = targets[t];
                    HidDeviceInfo iface = target.LongInterface ?? target.ShortInterface;
                    if (iface == null) continue;
                    using (HidppSession session = new HidppSession(iface, true))
                    {
                        if (!session.IsOpen) continue;
                        bool anyProtocol;
                        List<byte> indexes = Hidpp.ProbeDeviceIndexes(session, out anyProtocol);
                        if (indexes.Count == 0 && target.ShortInterface != null)
                        {
                            using (HidppSession legacy = new HidppSession(target.ShortInterface, true))
                            {
                                if (legacy.IsOpen) indexes = Hidpp.ProbeLegacyIndexes(legacy);
                            }
                        }
                        if (indexes.Count == 0) continue; // 接收器上没有设备应答

                        // 部分接收器对任意 index 都回同一台设备，按名称去重
                        Dictionary<string, bool> usedNames = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < indexes.Count; i++)
                        {
                            int index = indexes[i];
                            string address = Hidpp.BuildAddress(target.VidPid, index);
                            string id = "hid:" + address;
                            if (map.ContainsKey(id)) continue;

                            string name = null;
                            bool hasBattery = false;
                            try
                            {
                                name = Hidpp.DeviceName(session, (byte)index);
                                hasBattery = Hidpp.FeatureIndex(session, (byte)index, Hidpp.FeatureUnifiedBattery) > 0
                                          || Hidpp.FeatureIndex(session, (byte)index, Hidpp.FeatureBatteryLevelStatus) > 0
                                          || Hidpp.FeatureIndex(session, (byte)index, Hidpp.FeatureBatteryVoltage) > 0;
                            }
                            catch { }

                            string displayName = string.IsNullOrEmpty(name)
                                ? (target.VidPid.ToUpperInvariant() + (index == 0xFF ? "" : " #" + index))
                                : name;
                            if (usedNames.ContainsKey(displayName)) continue;
                            usedNames[displayName] = true;

                            DiscoveredDevice d = new DiscoveredDevice();
                            d.Id = id;
                            d.Name = displayName;
                            d.Source = hasBattery ? Sources.Hidpp : "";
                            d.Address = address;
                            d.Detail = hasBattery
                                ? "罗技 HID++ · 可自动读取电量"
                                : "罗技 HID++ · 该设备不提供电量数据";
                            map[id] = d;
                        }
                    }
                }
            }
            catch { }


            List<DiscoveredDevice> list = new List<DiscoveredDevice>(map.Values);
            list.Sort(delegate (DiscoveredDevice a, DiscoveredDevice b)
            {
                int sa = Order(a.Source), sb = Order(b.Source);
                if (sa != sb) return sa.CompareTo(sb);
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });
            return list;
        }

        private static int Order(string source)
        {
            if (source == Sources.Ble) return 0;
            if (source == Sources.Hidpp) return 1;
            if (source == Sources.Gamepad) return 2;
            if (source == Sources.Hid) return 3;
            return 4;
        }

        public static string Human(DateTime t)
        {
            if (t <= DateTime.MinValue || t.Year < 2000) return "未知";
            TimeSpan span = DateTime.Now - t;
            if (span.TotalMinutes < 1) return "刚刚";   // 1 分钟粒度
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " 分钟前";
            if (span.TotalHours < 24) return (int)span.TotalHours + " 小时前";
            if (span.TotalDays < 30) return (int)span.TotalDays + " 天前";
            return t.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        }

        public static string NormalizeAddress(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (Uri.IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
            }
            string hex = sb.ToString();
            if (hex.Length > 12) hex = hex.Substring(hex.Length - 12);
            return hex;
        }

        private static readonly Regex DevAddress = new Regex(@"DEV_([0-9A-Fa-f]{12})", RegexOptions.IgnoreCase);

        public static string ExtractAddress(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            Match m = DevAddress.Match(text);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            return "";
        }

        private static string ReadString(RegistryKey key, string name)
        {
            try
            {
                object v = key.GetValue(name);
                if (v is string) return ((string)v).Trim('\0', ' ');
            }
            catch { }
            return null;
        }

        private static string DecodeName(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            int len = bytes.Length;
            while (len > 0 && bytes[len - 1] == 0) len--;
            if (len == 0) return null;
            try { return Encoding.UTF8.GetString(bytes, 0, len).Trim(); }
            catch { return null; }
        }

        private static DateTime ReadFileTime(RegistryKey key, string name)
        {
            try
            {
                byte[] b = key.GetValue(name) as byte[];
                if (b == null || b.Length < 8) return DateTime.MinValue;
                long ft = BitConverter.ToInt64(b, 0);
                if (ft <= 0) return DateTime.MinValue;
                return DateTime.FromFileTimeUtc(ft).ToLocalTime();
            }
            catch { return DateTime.MinValue; }
        }
    }
}
