using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BatteryBar
{
    internal class AdvDevice
    {
        public string Address;
        public string Name;
        public int Left = -1;
        public int Right = -1;
        public int Case = -1;
        public bool Charging;
        public bool Apple;
        public bool FastPair;

        public int Level
        {
            get
            {
                if (Apple)
                {
                    if (Left >= 0 && Right >= 0) return Math.Min(Left, Right);
                    if (Left >= 0) return Left;
                    if (Right >= 0) return Right;
                    return Case;
                }
                if (Left >= 0) return Left;
                if (Right >= 0) return Right;
                return Case;
            }
        }

        public string Describe()
        {
            List<string> parts = new List<string>();
            if (Left >= 0) parts.Add("左 " + Left + "%");
            if (Right >= 0) parts.Add("右 " + Right + "%");
            if (Case >= 0) parts.Add("仓 " + Case + "%");
            string detail = parts.Count == 0 ? "" : " · " + string.Join(" / ", parts.ToArray());
            return (Apple ? "BLE 广播" : "Fast Pair") + detail;
        }
    }

    /// <summary>
    /// BLE 广播助手：调用随程序打包的 BleAgent.exe（PyInstaller 单文件，无 Python 依赖）
    /// 扫描附近的 BLE 广播，解析苹果 Proximity Pairing / Google Fast Pair 中的电量。
    /// </summary>
    internal static class BleAdvAgent
    {
        private static readonly object Gate = new object();
        private static List<AdvDevice> _cache = new List<AdvDevice>();
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheLife = TimeSpan.FromSeconds(25);
        private static bool _missing;

        public static string AgentPath
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new string[]
                {
                    Path.Combine(dir, "BleAgent.exe"),
                    Path.Combine(dir, "tools", "BleAgent.exe")
                };
                for (int i = 0; i < candidates.Length; i++)
                    if (File.Exists(candidates[i])) return candidates[i];
                return null;
            }
        }

        public static bool Available { get { return AgentPath != null; } }

        /// <summary>返回缓存的扫描结果（超过 25 秒自动重新扫描）。</summary>
        public static List<AdvDevice> Get(bool force)
        {
            lock (Gate)
            {
                if (!force && DateTime.Now - _cacheTime < CacheLife) return _cache;
                if (_missing && !force) return _cache;
                string exe = AgentPath;
                if (exe == null) { _missing = true; return _cache; }

                List<AdvDevice> list = Run(exe, 4);
                if (list != null)
                {
                    _cache = list;
                    _cacheTime = DateTime.Now;
                }
                return _cache;
            }
        }

        private static List<AdvDevice> Run(string exe, int seconds)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = exe;
                info.Arguments = "--seconds " + seconds;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;

                using (Process process = Process.Start(info))
                {
                    if (process == null) return null;
                    string json = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(20000))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }
                    return Parse(json);
                }
            }
            catch
            {
                return null;
            }
        }

        private static List<AdvDevice> Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            json = json.Trim();
            if (json.Length == 0 || json[0] != '[') return null;

            List<AdvDevice> list = new List<AdvDevice>();
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                object[] items = serializer.DeserializeObject(json) as object[];
                if (items == null) return list;
                for (int i = 0; i < items.Length; i++)
                {
                    Dictionary<string, object> item = items[i] as Dictionary<string, object>;
                    if (item == null) continue;
                    AdvDevice device = new AdvDevice();
                    device.Address = Str(item, "addr");
                    device.Name = Str(item, "name");

                    Dictionary<string, object> apple = item.ContainsKey("apple") ? item["apple"] as Dictionary<string, object> : null;
                    if (apple != null)
                    {
                        device.Apple = true;
                        device.Left = Int(apple, "left");
                        device.Right = Int(apple, "right");
                        device.Case = Int(apple, "case");
                        Dictionary<string, object> charging = apple.ContainsKey("charging") ? apple["charging"] as Dictionary<string, object> : null;
                        if (charging != null)
                        {
                            device.Charging = Bool(charging, "left") || Bool(charging, "right") || Bool(charging, "case");
                        }
                        if (device.Name.Length == 0) device.Name = "AirPods";
                    }

                    Dictionary<string, object> fastpair = item.ContainsKey("fastpair") ? item["fastpair"] as Dictionary<string, object> : null;
                    if (fastpair != null && !device.Apple)
                    {
                        device.FastPair = true;
                        object[] battery = fastpair.ContainsKey("battery") ? fastpair["battery"] as object[] : null;
                        if (battery != null)
                        {
                            if (battery.Length > 0) device.Left = ToInt(battery[0]);
                            if (battery.Length > 1) device.Right = ToInt(battery[1]);
                            if (battery.Length > 2) device.Case = ToInt(battery[2]);
                        }
                        if (device.Name.Length == 0) device.Name = "Fast Pair 耳机";
                    }

                    if (device.Apple || device.FastPair) list.Add(device);
                }
            }
            catch
            {
                return null;
            }
            return list;
        }

        private static string Str(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key) || dict[key] == null) return "";
            return Convert.ToString(dict[key]);
        }

        private static int Int(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key)) return -1;
            return ToInt(dict[key]);
        }

        private static int ToInt(object value)
        {
            if (value == null) return -1;
            try
            {
                int parsed;
                if (int.TryParse(Convert.ToString(value), out parsed)) return parsed;
            }
            catch { }
            return -1;
        }

        private static bool Bool(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key) || dict[key] == null) return false;
            try { return Convert.ToBoolean(dict[key]); }
            catch { return false; }
        }
    }

    internal class BleAdvProvider : IProvider
    {
        public Reading Read(DeviceConfig device)
        {
            if (!BleAdvAgent.Available) return Reading.Fail("缺少 BLE 广播助手（BleAgent.exe）", false);

            List<AdvDevice> devices = BleAdvAgent.Get(false);
            if (devices == null || devices.Count == 0) return Reading.Sleep("未收到 BLE 广播 · 确认耳机在附近");

            for (int i = 0; i < devices.Count; i++)
            {
                AdvDevice adv = devices[i];
                if (!string.Equals(adv.Address, device.Address, StringComparison.OrdinalIgnoreCase)) continue;
                if (adv.Apple)
                {
                    // 实测发现：AirPods 的 Proximity Pairing 广播里电量字段是加密/轮换的，
                    // 同一台设备两次扫描得到的值随机变化，不能作为电量显示。
                    return Reading.Fail("该型号的苹果广播电量已加密，读不到真实电量", false);
                }
                int level = adv.Level;
                if (level < 0) return Reading.Fail("广播中没有电量字段", true);
                Reading reading = Reading.Good(level, adv.Describe(), adv.Charging);
                reading.Connected = true;
                return reading;
            }
            return Reading.Sleep("未收到广播 · 打开耳机盖或戴上耳机试试");
        }
    }
}
