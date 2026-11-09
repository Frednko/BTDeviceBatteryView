using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryBar
{
    /// <summary>
    /// 通用 HID 电量：读取 HID 标准用法 Generic Device Controls(0x06) / Battery Strength(0x20)。
    /// 任何实现了该用法的设备（部分鼠标、键盘、耳机、手柄）都能被自动识别，不需要为型号写专门代码。
    /// </summary>
    internal static class HidBattery
    {
        private const int HIDP_INPUT = 0;
        private const int HIDP_FEATURE = 2;
        private const ushort USAGE_PAGE_GENERIC = 0x0006;
        private const ushort USAGE_BATTERY_STRENGTH = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
            public byte IsAlias;
            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;
            public byte HasNull;
            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;
            public ushort Reserved2a;
            public ushort Reserved2b;
            public ushort Reserved2c;
            public ushort Reserved2d;
            public ushort Reserved2e;
            public uint UnitsExp;
            public uint Units;
            public int LogicalMin;
            public int LogicalMax;
            public int PhysicalMin;
            public int PhysicalMax;
            public ushort UsageMin;
            public ushort UsageMax;
            public ushort StringMin;
            public ushort StringMax;
            public ushort DesignatorMin;
            public ushort DesignatorMax;
            public ushort DataIndexMin;
            public ushort DataIndexMax;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
        }

        [DllImport("hid.dll")]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsed);
        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")]
        private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsed, ref HIDP_CAPS caps);
        [DllImport("hid.dll")]
        private static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] valueCaps, ref ushort length, IntPtr preparsed);
        [DllImport("hid.dll")]
        private static extern int HidP_GetUsageValue(int reportType, byte reportId, ushort collection,
            ushort usagePage, ushort usage, out uint value, IntPtr preparsed, byte[] report, int reportLength);

        /// <summary>读取设备电量；返回 -1 表示该设备没有标准电量用法。</summary>
        public static int ReadLevel(HidDeviceInfo info, out bool charging)
        {
            charging = false;
            using (SafeFileHandle handle = Native.OpenDevice(info.Path, false))
            {
                if (handle.IsInvalid) return -1;
                IntPtr preparsed;
                if (!HidD_GetPreparsedData(handle, out preparsed)) return -1;
                try
                {
                    HIDP_CAPS caps = new HIDP_CAPS();
                    caps.Reserved = new ushort[17];
                    if (HidP_GetCaps(preparsed, ref caps) < 0) return -1;

                    int level = TryReport(handle, preparsed, caps, HIDP_FEATURE);
                    if (level >= 0) return level;
                    level = TryReport(handle, preparsed, caps, HIDP_INPUT);
                    return level;
                }
                finally { HidD_FreePreparsedData(preparsed); }
            }
        }

        private static int TryReport(SafeFileHandle handle, IntPtr preparsed, HIDP_CAPS caps, int reportType)
        {
            ushort count = 0;
            HIDP_VALUE_CAPS probe = new HIDP_VALUE_CAPS();
            HIDP_VALUE_CAPS[] dummy = new HIDP_VALUE_CAPS[1];
            int status = HidP_GetValueCaps(reportType, dummy, ref count, preparsed);
            if (count == 0 || count > 512) return -1;

            HIDP_VALUE_CAPS[] valueCaps = new HIDP_VALUE_CAPS[count];
            ushort length = count;
            if (HidP_GetValueCaps(reportType, valueCaps, ref length, preparsed) < 0) return -1;

            int reportLength = reportType == HIDP_FEATURE ? caps.FeatureReportByteLength : caps.InputReportByteLength;
            if (reportLength <= 0) return -1;

            for (int i = 0; i < length; i++)
            {
                HIDP_VALUE_CAPS cap = valueCaps[i];
                if (cap.UsagePage != USAGE_PAGE_GENERIC) continue;
                bool match;
                if (cap.IsRange != 0)
                    match = USAGE_BATTERY_STRENGTH >= cap.UsageMin && USAGE_BATTERY_STRENGTH <= cap.UsageMax;
                else
                    match = cap.UsageMin == USAGE_BATTERY_STRENGTH;
                if (!match) continue;

                byte[] report = new byte[reportLength];
                report[0] = cap.ReportID;
                if (reportType == HIDP_FEATURE)
                {
                    if (!HidD_GetFeature(handle, report, reportLength)) continue;
                }
                else
                {
                    if (!ReadInputReport(handle, report, reportLength, cap.ReportID)) continue;
                }

                uint value;
                if (HidP_GetUsageValue(reportType, cap.ReportID, cap.LinkCollection, USAGE_PAGE_GENERIC,
                        USAGE_BATTERY_STRENGTH, out value, preparsed, report, reportLength) < 0)
                    continue;

                int min = cap.LogicalMin;
                int max = cap.LogicalMax;
                int level = (int)value;
                if (max > min)
                {
                    level = (int)Math.Round((value - (double)min) * 100.0 / (max - min));
                }
                else if (level > 100 && level <= 255)
                {
                    level = (int)Math.Round(level * 100.0 / 255.0);
                }
                if (level < 0) level = 0;
                if (level > 100) level = 100;
                return level;
            }
            return -1;
        }

        private static bool ReadInputReport(SafeFileHandle handle, byte[] report, int length, byte reportId)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int read;
                if (!NativeRead(handle, report, length, out read)) return false;
                if (read <= 0) continue;
                if (report[0] == reportId) return true;
            }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle h, byte[] buffer, int count, out int read, ref OVERLAPPED ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr attrs, bool manualReset, bool initialState, IntPtr name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeFileHandle h, ref OVERLAPPED ov, out int transferred, bool wait);
        [DllImport("kernel32.dll")]
        private static extern bool CancelIo(SafeFileHandle h);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private const int ERROR_IO_PENDING = 997;

        private static bool NativeRead(SafeFileHandle handle, byte[] buffer, int length, out int read)
        {
            read = 0;
            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            try
            {
                OVERLAPPED ov = new OVERLAPPED();
                ov.hEvent = evt;
                if (!ReadFile(handle, buffer, length, out read, ref ov))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_IO_PENDING) return false;
                    if (WaitForSingleObject(evt, 250) != 0) { CancelIo(handle); return false; }
                    int transferred;
                    GetOverlappedResult(handle, ref ov, out transferred, false);
                    read = transferred;
                }
                return read > 0;
            }
            finally { if (evt != IntPtr.Zero) CloseHandle(evt); }
        }
    }

    internal class HidBatteryProvider : IProvider
    {
        public Reading Read(DeviceConfig device)
        {
            List<HidDeviceInfo> all = HidDevices.Enumerate();
            bool matchedAny = false;
            for (int i = 0; i < all.Count; i++)
            {
                HidDeviceInfo info = all[i];
                if (!string.IsNullOrEmpty(device.Address) &&
                    !string.Equals(HidDevices.VidPid(info), device.Address, StringComparison.OrdinalIgnoreCase)) continue;
                matchedAny = true;
                bool charging;
                int level = HidBattery.ReadLevel(info, out charging);
                if (level >= 0)
                {
                    Reading reading = Reading.Good(level, charging ? "HID 电量 · 充电中" : "HID 电量");
                    return reading;
                }
            }
            if (!matchedAny) return Reading.Sleep("设备未连接");
            return Reading.Fail("该设备未提供 HID 电量字段", false);
        }
    }
}
