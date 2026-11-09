using System;
using System.Collections.Generic;

namespace BatteryBar
{
    /// <summary>
    /// 常见蓝牙设备的电量来源预设表。
    /// 作用：新设备先按型号/品牌归族，告诉用户「这台设备理论上该怎么读、现在能不能读」，
    /// 而不是笼统地显示「无法识别」。新增协议时只需要在 Detection 里补一条实现。
    /// </summary>
    internal class DeviceRule
    {
        public string Family;       // 设备族名称
        public string[] Patterns;   // 名称关键字（小写包含匹配）
        public string Transport;    // 电量从哪里来
        public bool Supported;      // 当前版本是否已实现

        public bool Matches(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            for (int i = 0; i < Patterns.Length; i++)
                if (lower.IndexOf(Patterns[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }

    internal static class DeviceRules
    {
        private static readonly List<DeviceRule> Rules = Build();

        private static List<DeviceRule> Build()
        {
            List<DeviceRule> list = new List<DeviceRule>();

            // —— 已实现 ——
            list.Add(new DeviceRule
            {
                Family = "罗技无线设备",
                Patterns = new string[] { "logitech", "罗技", "usb receiver", "lightspeed" },
                Transport = "罗技 HID++（0x1004/0x1000/0x1001/1.0 寄存器）",
                Supported = true
            });
            list.Add(new DeviceRule
            {
                Family = "PlayStation 手柄",
                Patterns = new string[] { "dualsense", "dualshock", "wireless controller", "ps5", "ps4" },
                Transport = "手柄 HID 输入报文（0x31/0x01）",
                Supported = true
            });
            list.Add(new DeviceRule
            {
                Family = "Xbox 手柄",
                Patterns = new string[] { "xbox" },
                Transport = "HID Battery Strength（0x06/0x20）",
                Supported = true
            });
            list.Add(new DeviceRule
            {
                Family = "通用 BLE 设备",
                Patterns = new string[] { "ble", "低功耗" },
                Transport = "蓝牙 GATT 电池服务 0x180F/0x2A19",
                Supported = true
            });

            // —— 需要额外传输通道，暂未实现 ——
            list.Add(new DeviceRule
            {
                Family = "Sony 耳机",
                Patterns = new string[] { "sony", "wh-", "wf-", "wi-", "xm4", "xm5", "1000x", "linkbuds" },
                Transport = "Sony SPP 私有协议（RFCOMM）或 HFP 电池扩展",
                Supported = false
            });
            list.Add(new DeviceRule
            {
                Family = "Apple 耳机",
                Patterns = new string[] { "airpods", "beats", "powerbeats", "apple" },
                Transport = "Apple 私有 AAP 协议（Windows 不支持 L2CAP，广播电量已加密）",
                Supported = false
            });
            list.Add(new DeviceRule
            {
                Family = "小米 / Redmi 耳机",
                Patterns = new string[] { "xiaomi", "redmi", "小米", "红米", "mi buds", "airdots" },
                Transport = "BLE 广播（Fast Pair 0xFE2C）",
                Supported = true
            });
            list.Add(new DeviceRule
            {
                Family = "HFP 电池扩展耳机",
                Patterns = new string[] { "jbl", "bose", "jabra", "sennheiser", "momentum", "soundcore", "anker", "edifier", "漫步者" },
                Transport = "HFP AT+XAPL / AT+IPHONEACCEV（RFCOMM）",
                Supported = false
            });
            return list;
        }

        public static DeviceRule Match(string name)
        {
            for (int i = 0; i < Rules.Count; i++)
                if (Rules[i].Matches(name)) return Rules[i];
            return null;
        }

        /// <summary>给界面用的一句话解释。</summary>
        public static string Hint(string name)
        {
            DeviceRule rule = Match(name);
            if (rule == null) return "该设备不上报电量（没有标准电量接口）";
            if (rule.Supported) return rule.Family + " · 通过 " + rule.Transport + " 读取";
            return rule.Family + " · 需要 " + rule.Transport + "，当前版本暂未实现";
        }
    }
}
