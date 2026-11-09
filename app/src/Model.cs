using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BatteryBar
{
    internal static class Sources
    {
        public const string Ble = "ble";         // 蓝牙低功耗 GATT 电池服务
        public const string Hidpp = "hidpp";     // 罗技 HID++
        public const string Gamepad = "gamepad"; // PlayStation 手柄输入报文
        public const string Hid = "hid";         // 通用 HID 电量（Battery Strength）
        public const string Adv = "adv";         // BLE 广播（Apple / Fast Pair 耳机）
        public const string Unknown = "unknown"; // 未识别

        public static string Display(string source)
        {
            switch (source)
            {
                case Ble: return "蓝牙 GATT";
                case Hidpp: return "罗技 HID++";
                case Gamepad: return "手柄 HID";
                case Hid: return "HID 电量";
                case Adv: return "BLE 广播";
                default: return "无法识别";
            }
        }
    }

    [DataContract]
    internal class DeviceConfig
    {
        [DataMember(Order = 1)] public string Id;
        [DataMember(Order = 2)] public string Name;
        [DataMember(Order = 3)] public string Source = "";
        [DataMember(Order = 4)] public string Address;
        [DataMember(Order = 5)] public bool ShowInTray;
        [DataMember(Order = 6)] public int ManualLevel = 80; // 旧版本字段，保留兼容
        [DataMember(Order = 7)] public int LastLevel = -1;
        [DataMember(Order = 8)] public long LastUpdateTicks;
        [DataMember(Order = 9)] public bool LastConnected;
        [DataMember(Order = 10)] public bool Enabled = true;
        [DataMember(Order = 11)] public int LowThreshold; // 0 = 跟随全局
        [DataMember(Order = 12)] public string AutoSource = ""; // 旧版本字段，保留兼容
        [DataMember(Order = 13)] public bool Charging;

        // ---- 运行时状态（不落盘） ----
        [IgnoreDataMember] public string StatusText = "尚未读取";
        [IgnoreDataMember] public long LastAttemptTicks;   // 上次尝试读取的时间
        [IgnoreDataMember] public bool SleepPending;       // 上次读取失败（休眠中），需要快速重试
        [IgnoreDataMember] public int SleepRetries;        // 连续快速重试次数
        [IgnoreDataMember] public bool WakeProbed;         // 本轮唤醒是否已经探测过
        [IgnoreDataMember] public int WakeProbeCount;      // 本轮唤醒的探测次数（上限 3）
        [IgnoreDataMember] public int FailStrikes;         // 连续失败次数（连续 3 次才认定休眠）
        [IgnoreDataMember] public string ErrorText;
        [IgnoreDataMember] public bool Busy;
        [IgnoreDataMember] public bool UnknownSource;

        [IgnoreDataMember]
        public DateTime LastUpdate
        {
            get { return LastUpdateTicks <= 0 ? DateTime.MinValue : new DateTime(LastUpdateTicks, DateTimeKind.Utc).ToLocalTime(); }
            set { LastUpdateTicks = value.ToUniversalTime().Ticks; }
        }

        [IgnoreDataMember]
        public bool IsAuto { get { return Source == Sources.Ble || Source == Sources.Hidpp || Source == Sources.Gamepad || Source == Sources.Hid || Source == Sources.Adv; } }

        [IgnoreDataMember]
        public int Level { get { return LastLevel; } }

        [IgnoreDataMember]
        public bool IsCharging { get { return Charging; } }

        [IgnoreDataMember]
        public string Subtitle
        {
            get
            {
                if (Busy) return "正在读取…";
                if (!string.IsNullOrEmpty(ErrorText)) return ErrorText;
                if (UnknownSource) return "无法识别 · 该设备不上报电量";
                return StatusText;
            }
        }

        [IgnoreDataMember]
        public int EffectiveLowThreshold { get; set; }
    }

    [DataContract]
    internal class AppConfig
    {
        [DataMember(Order = 1)] public List<DeviceConfig> Devices = new List<DeviceConfig>();
        [DataMember(Order = 2)] public string TrayDeviceId = "";
        [DataMember(Order = 3)] public string SelectedDeviceId = "";
        [DataMember(Order = 4)] public int LowThreshold = 20;
        [DataMember(Order = 5)] public int PollSeconds = 120;
        [DataMember(Order = 6)] public int NotifyCooldownMinutes = 180;
        [DataMember(Order = 7)] public bool AutoStart = true;
        [DataMember(Order = 8)] public int IconStyle = 0; // 0 = 电池图标, 1 = 百分比数字
        [DataMember(Order = 9)] public bool Notifications = true;

        public DeviceConfig Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Devices.Count; i++)
                if (Devices[i].Id == id) return Devices[i];
            return null;
        }

        public DeviceConfig TrayDevice
        {
            get
            {
                if (string.IsNullOrEmpty(TrayDeviceId)) return null;
                return Find(TrayDeviceId);
            }
        }

        public void SetTrayDevice(string id)
        {
            TrayDeviceId = id;
            for (int i = 0; i < Devices.Count; i++)
                Devices[i].ShowInTray = Devices[i].Id == id;
        }
    }

    internal static class ConfigStore
    {
        public static string Dir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryBar");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "config.json"); } }

        public static AppConfig Load()
        {
            AppConfig cfg = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    using (FileStream fs = File.OpenRead(FilePath))
                    {
                        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppConfig));
                        cfg = (AppConfig)ser.ReadObject(fs);
                    }
                }
            }
            catch { cfg = null; }

            if (cfg == null) cfg = new AppConfig();
            if (cfg.Devices == null) cfg.Devices = new List<DeviceConfig>();
            if (cfg.PollSeconds < 15) cfg.PollSeconds = 120;
            if (cfg.LowThreshold < 1 || cfg.LowThreshold > 90) cfg.LowThreshold = 20;
            for (int i = cfg.Devices.Count - 1; i >= 0; i--)
                if (cfg.Devices[i] == null || string.IsNullOrEmpty(cfg.Devices[i].Id)) cfg.Devices.RemoveAt(i);
            return cfg;
        }

        public static void Save(AppConfig cfg)
        {
            try
            {
                string tmp = FilePath + ".tmp";
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppConfig));
                    ser.WriteObject(fs, cfg);
                }
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { }
        }
    }
}
