using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryBar
{
    /// <summary>
    /// PlayStation 手柄电量：控制器在主输入报文里上报电量。
    /// DualSense：USB 报文 0x01 的 byte 53 / 蓝牙报文 0x31 的 byte 54，
    /// 低 4 位为电量档位（0-9 表示 0-90%，10 表示 100%），高 4 位为充电状态。
    /// DualShock4：USB 0x01 byte 30 / 蓝牙 0x11 byte 32，编码相同。
    /// </summary>
    internal static class GamepadBattery
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFileNative(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
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

        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const int ERROR_IO_PENDING = 997;

        private static readonly object Gate = new object();

        /// <summary>是否为已知会上报电量的 PlayStation 手柄。</summary>
        public static bool IsSupported(ushort vid, ushort pid)
        {
            if (vid != 0x054C) return false;
            switch (pid)
            {
                case 0x0CE6: // DualSense
                case 0x0DF2: // DualSense Edge
                case 0x05C4: // DualShock 4
                case 0x09CC: // DualShock 4 v2
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDualSense(ushort pid)
        {
            return pid == 0x0CE6 || pid == 0x0DF2;
        }

        /// <summary>找到手柄的主输入接口（Usage Page 1 / Usage 5）。</summary>
        public static HidDeviceInfo FindInterface(string vidPid)
        {
            List<HidDeviceInfo> all = HidDevices.Enumerate();
            HidDeviceInfo best = null;
            for (int i = 0; i < all.Count; i++)
            {
                HidDeviceInfo info = all[i];
                if (!string.Equals(HidDevices.VidPid(info), vidPid, StringComparison.OrdinalIgnoreCase)) continue;
                if (info.UsagePage != 0x0001 || info.Usage != 0x0005) continue;
                if (info.InputLength < 30) continue;
                if (best == null || info.InputLength > best.InputLength) best = info;
            }
            return best;
        }

        /// <summary>读取一份输入报文并解析电量。</summary>
        public static Reading Read(string vidPid, out bool sleeping)
        {
            sleeping = false;
            lock (Gate)
            {
                HidDeviceInfo info = FindInterface(vidPid);
                if (info == null)
                {
                    sleeping = true;
                    return Reading.Sleep("手柄未连接");
                }

                byte[] report = ReadReport(info);
                if (report == null)
                {
                    sleeping = true;
                    return Reading.Sleep("手柄休眠中 · 按一下任意键");
                }

                bool dualSense = IsDualSense(info.ProductId);
                int offset = -1;
                if (dualSense)
                {
                    if (report[0] == 0x01) offset = 53;        // USB
                    else if (report[0] == 0x31) offset = 54;   // 蓝牙
                }
                else
                {
                    if (report[0] == 0x01) offset = 30;        // DS4 USB
                    else if (report[0] == 0x11) offset = 32;   // DS4 蓝牙
                }

                if (offset < 0 || offset >= report.Length)
                    return Reading.Fail("无法识别手柄报文", false);

                int status = report[offset];
                int fleet = status & 0x0F;
                int charging = (status >> 4) & 0x0F;
                int percent;
                string note = "";

                if (charging == 0x02)
                {
                    percent = 100;
                    note = "已充满";
                }
                else if (charging == 0x0A || charging == 0x0B || charging == 0x0F)
                {
                    percent = Math.Min(fleet * 10 + 5, 100);
                    note = "充电异常";
                }
                else
                {
                    percent = Math.Min(fleet * 10 + 5, 100);
                    if (charging == 0x01) note = "充电中";
                }
                if (percent > 100) percent = 100;
                if (percent < 0) percent = 0;

                string name = dualSense ? "DualSense" : "DualShock 4";
                bool isCharging = charging == 0x01 || charging == 0x02;
                Reading reading = Reading.Good(percent, note.Length > 0 ? name + " · " + note : name, isCharging);
                reading.Connected = true;
                return reading;
            }
        }

        private static byte[] ReadReport(HidDeviceInfo info)
        {
            int length = info.InputLength;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using (SafeFileHandle handle = CreateFileNative(info.Path, 0x80000000u, 3u, IntPtr.Zero, 3u, FILE_FLAG_OVERLAPPED, IntPtr.Zero))
                {
                    if (handle.IsInvalid) return null;
                    IntPtr evt = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                    try
                    {
                        byte[] buffer = new byte[length];
                        int read;
                        OVERLAPPED ov = new OVERLAPPED();
                        ov.hEvent = evt;
                        if (!ReadFile(handle, buffer, length, out read, ref ov))
                        {
                            int err = Marshal.GetLastWin32Error();
                            if (err != ERROR_IO_PENDING) return null;
                            if (WaitForSingleObject(evt, 500) != 0)
                            {
                                CancelIo(handle);
                                continue;
                            }
                            int transferred;
                            GetOverlappedResult(handle, ref ov, out transferred, false);
                            read = transferred;
                        }
                        if (read >= 30) return buffer;
                    }
                    finally { if (evt != IntPtr.Zero) CloseHandle(evt); }
                }
            }
            return null;
        }
    }

    internal class GamepadProvider : IProvider
    {
        public Reading Read(DeviceConfig device)
        {
            bool sleeping;
            Reading reading = GamepadBattery.Read(device.Address, out sleeping);
            return reading;
        }
    }
}
