using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryBar
{
    /// <summary>
    /// 罗技 HID++ 通信。
    /// 2.0（FAP）走长报文 0x11，1.0（RAP）走短报文 0x10；
    /// 接收器把 index 1..6 的报文转发给对应配对设备，0xFF 是接收器自身。
    /// </summary>
    internal class HidppSession : IDisposable
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
        private static extern bool WriteFile(SafeFileHandle h, byte[] buffer, int count, out int written, ref OVERLAPPED ov);
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

        private readonly SafeFileHandle _handle;
        private readonly IntPtr _event;
        private readonly int _inLength;
        private readonly int _outLength;
        private readonly byte _reportId;
        public bool IsOpen { get { return _handle != null && !_handle.IsInvalid; } }

        public HidppSession(HidDeviceInfo info, bool forWrite)
        {
            _inLength = Math.Max(7, info.InputLength);
            _outLength = Math.Max(_inLength, info.OutputLength);
            _reportId = _inLength > 8 ? (byte)0x11 : (byte)0x10;
            _handle = CreateFileNative(info.Path, forWrite ? 0xC0000000u : 0x80000000u, 3u, IntPtr.Zero, 3u, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            _event = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        }

        /// <summary>发送 2.0 报文并等待 feature/func 完全匹配的应答（跳过通知）。</summary>
        public byte[] Feature(byte device, byte feature, byte funcByte, byte[] parameters, int timeoutMs)
        {
            byte[] report = new byte[_outLength];
            report[0] = _reportId;
            report[1] = device;
            report[2] = feature;
            report[3] = funcByte;
            if (parameters != null) Array.Copy(parameters, 0, report, 4, Math.Min(parameters.Length, _outLength - 4));
            return Exchange(report, device, feature, funcByte, timeoutMs);
        }

        /// <summary>发送 1.0（RAP）报文：subId 0x81 = 读寄存器，0x80 = 写寄存器。</summary>
        public byte[] Register(byte device, byte subId, byte register, byte[] parameters, int timeoutMs)
        {
            byte[] report = new byte[_outLength];
            report[0] = _reportId;
            report[1] = device;
            report[2] = subId;
            report[3] = register;
            if (parameters != null) Array.Copy(parameters, 0, report, 4, Math.Min(parameters.Length, _outLength - 4));
            return Exchange(report, device, subId, register, timeoutMs);
        }

        private byte[] Exchange(byte[] report, byte device, byte tag1, byte tag2, int timeoutMs)
        {
            if (!IsOpen) return null;
            OVERLAPPED ov = new OVERLAPPED();
            ov.hEvent = _event;
            int written;
            if (!WriteFile(_handle, report, report.Length, out written, ref ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING) return null;
                if (WaitForSingleObject(_event, 200) != 0) { CancelIo(_handle); return null; }
                int transferred;
                GetOverlappedResult(_handle, ref ov, out transferred, false);
            }
            Thread.Sleep(8);

            int deadline = Environment.TickCount + Math.Max(120, timeoutMs);
            while (Environment.TickCount < deadline)
            {
                byte[] buffer = new byte[_inLength];
                int read;
                OVERLAPPED ov2 = new OVERLAPPED();
                ov2.hEvent = _event;
                if (!ReadFile(_handle, buffer, buffer.Length, out read, ref ov2))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_IO_PENDING) return null;
                    if (WaitForSingleObject(_event, 120) != 0) { CancelIo(_handle); break; }
                    int transferred;
                    GetOverlappedResult(_handle, ref ov2, out transferred, false);
                    read = transferred;
                }
                if (read < 4) continue;
                if (buffer[0] != _reportId || buffer[1] != device) continue;
                if (buffer[2] == tag1 && buffer[3] == tag2) return Trim(buffer, read);
                if (buffer[2] == 0xFF || buffer[2] == 0x8F) return Trim(buffer, read); // 错误应答
            }
            return null;
        }

        private static byte[] Trim(byte[] buffer, int read)
        {
            byte[] result = new byte[read];
            Array.Copy(buffer, result, read);
            return result;
        }

        public void Dispose()
        {
            try { if (_event != IntPtr.Zero) CloseHandle(_event); } catch { }
            try { if (_handle != null) _handle.Dispose(); } catch { }
        }
    }

    internal class HidppTarget
    {
        public HidDeviceInfo LongInterface;
        public HidDeviceInfo ShortInterface;
        public string VidPid;

        public string Key { get { return VidPid; } }
    }

    internal class HidppBattery
    {
        public int Level = -1;
        public bool Connected;
        public string Detail = "";
        public bool Charging;
    }

    internal static class Hidpp
    {
        public const int FeatureBatteryLevelStatus = 0x1000;
        public const int FeatureBatteryVoltage = 0x1001;
        public const int FeatureUnifiedBattery = 0x1004;
        public const int FeatureDeviceName = 0x0005;

        /// <summary>把系统中的罗技 HID++ 接口按 VID:PID 分组（每个接收器一组）。</summary>
        public static List<HidppTarget> FindTargets()
        {
            List<HidppTarget> targets = new List<HidppTarget>();
            Dictionary<string, HidppTarget> map = new Dictionary<string, HidppTarget>();
            List<HidDeviceInfo> all = HidDevices.Enumerate();
            for (int i = 0; i < all.Count; i++)
            {
                HidDeviceInfo info = all[i];
                if (info.VendorId != 0x046D || info.UsagePage != 0xFF00 || info.InputLength < 7) continue;
                string key = HidDevices.VidPid(info);
                HidppTarget target;
                if (!map.TryGetValue(key, out target))
                {
                    target = new HidppTarget();
                    target.VidPid = key;
                    map[key] = target;
                    targets.Add(target);
                }
                if (info.InputLength > 8)
                {
                    if (target.LongInterface == null || info.InputLength > target.LongInterface.InputLength) target.LongInterface = info;
                }
                else
                {
                    if (target.ShortInterface == null) target.ShortInterface = info;
                }
            }
            return targets;
        }

        /// <summary>查询接口上某个 index 的 HID++ 版本，返回 0 表示无设备应答。</summary>
        public static int ProtocolMajor(HidppSession session, byte index)
        {
            byte[] reply = session.Feature(index, 0x00, 0x11, null, 200);
            if (reply == null || reply.Length < 6) return 0;
            if (reply[2] >= 0x80) return 0; // 0xFF=2.0 错误，0x8F=1.0 错误
            return reply[4];
        }

        public static int FeatureIndex(HidppSession session, byte index, int featureId)
        {
            byte[] parameters = new byte[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF) };
            byte[] reply = session.Feature(index, 0x00, 0x01, parameters, 200);
            if (reply == null || reply.Length < 5) return 0;
            if (reply[2] >= 0x80) return 0;
            return reply[4];
        }

        public static string DeviceName(HidppSession session, byte index)
        {
            int feature = FeatureIndex(session, index, FeatureDeviceName);
            if (feature == 0) return null;
            byte[] countReply = session.Feature(index, (byte)feature, 0x00, null, 200);
            if (countReply == null || countReply.Length < 5) return null;
            int length = countReply[4];
            if (length <= 0 || length > 64) return null;

            // 不同固件对参数的解释不同：有的按字节偏移，有的按 16 字节分块
            string byOffset = ReadNameChunks(session, index, feature, length, 16);
            if (byOffset != null && byOffset.Length == length) return byOffset;
            string byChunk = ReadNameChunks(session, index, feature, length, 1);
            if (byChunk != null && byChunk.Length == length) return byChunk;
            return !string.IsNullOrEmpty(byOffset) ? byOffset : byChunk;
        }

        private static string ReadNameChunks(HidppSession session, byte index, int feature, int length, int step)
        {
            StringBuilder sb = new StringBuilder();
            for (int part = 0; part < 8 && sb.Length < length; part++)
            {
                int parameter = part * step;
                byte[] reply = session.Feature(index, (byte)feature, 0x10, new byte[] { (byte)parameter }, 200);
                if (reply == null || reply.Length < 5) return sb.Length > 0 ? sb.ToString() : null;
                for (int k = 4; k < reply.Length && sb.Length < length; k++)
                {
                    char c = (char)reply[k];
                    if (c == '\0') return sb.ToString();
                    if (c < 32) return sb.ToString();
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>读取某个设备的电量（自动尝试 0x1004 / 0x1000 / 0x1001 与 HID++ 1.0）。</summary>
        /// <summary>
        /// 读取电量：设备可能是「醒着但这一帧被 G HUB 抢走」，所以给最多 3 次机会，
        /// 避免偶发的单次失败被误判成「设备休眠」。
        /// </summary>
        public static HidppBattery ReadBattery(HidppSession session, byte index)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                HidppBattery reading = ReadBatteryOnce(session, index);
                if (reading.Level >= 0) return reading;
                if (attempt < 2) Thread.Sleep(80);
            }
            return new HidppBattery();
        }

        private static HidppBattery ReadBatteryOnce(HidppSession session, byte index)
        {
            HidppBattery result = new HidppBattery();

            // 先用最轻的 root 查询探一下设备是否醒着：
            // 没醒就直接返回，不向休眠中的设备反复发电池请求，避免干扰固件/G HUB
            if (ProtocolMajor(session, index) == 0) return result;

            // 1) Unified Battery (0x1004)：SUPERLIGHT、MX Master 3S 等新设备
            int feature = FeatureIndex(session, index, FeatureUnifiedBattery);
            if (feature > 0)
            {
                byte[] status = session.Feature(index, (byte)feature, 0x10, null, 250);
                if (status != null && status.Length >= 8 && status[2] < 0x80)
                {
                    int level = status[4];
                    // 设备刚醒 / 正在休眠时，接收器可能回一帧 0x0F 填充的“假数据”（会被误读成 15%），
                    // 用其它字段一起做合理性判断，避免把垃圾数据当成电量
                    bool plausible = level > 0 && level <= 100
                        && status[5] != 0x0F && status[6] <= 3 && status[7] != 0x0F;
                    if (plausible)
                    {
                        result.Level = level;
                        result.Connected = true;
                        int chargeStatus = status[6];
                        result.Charging = chargeStatus == 1 || chargeStatus == 2;
                        result.Detail = result.Charging ? "充电中" : (chargeStatus == 3 ? "已充满" : "");
                        return result;
                    }
                }
            }

            // 2) Battery Level Status (0x1000)
            feature = FeatureIndex(session, index, FeatureBatteryLevelStatus);
            if (feature > 0)
            {
                byte[] status = session.Feature(index, (byte)feature, 0x00, null, 250);
                if (status != null && status.Length >= 6 && status[2] < 0x80)
                {
                    int level = status[4];
                    if (level > 0 && level <= 100 && status[5] != 0x0F)
                    {
                        result.Level = level;
                        result.Connected = true;
                        return result;
                    }
                }
            }

            // 3) Battery Voltage (0x1001)：只有电压时按锂电曲线估算
            feature = FeatureIndex(session, index, FeatureBatteryVoltage);
            if (feature > 0)
            {
                byte[] status = session.Feature(index, (byte)feature, 0x00, null, 250);
                if (status != null && status.Length >= 6 && status[2] < 0x80)
                {
                    int millivolts = (status[4] << 8) | status[5];
                    if (millivolts > 2000 && millivolts < 5000)
                    {
                        result.Level = VoltageToPercent(millivolts);
                        result.Connected = true;
                        result.Detail = millivolts + " mV";
                        return result;
                    }
                }
            }

            // 4) HID++ 1.0：寄存器 0x0D（百分比）/ 0x07（四档电量）
            byte[] mileage = session.Register(index, 0x81, 0x0D, null, 250);
            if (mileage != null && mileage.Length >= 6 && mileage[2] == 0x81 && mileage[3] == 0x0D)
            {
                int level = mileage[4];
                if (level > 0 && level <= 100)
                {
                    result.Level = level;
                    result.Connected = true;
                    return result;
                }
            }
            byte[] statusLegacy = session.Register(index, 0x81, 0x07, null, 250);
            if (statusLegacy != null && statusLegacy.Length >= 6 && statusLegacy[2] == 0x81 && statusLegacy[3] == 0x07)
            {
                int code = statusLegacy[4];
                int level = code >= 7 ? 90 : code >= 5 ? 55 : code >= 3 ? 20 : code >= 1 ? 5 : -1;
                if (level > 0)
                {
                    result.Level = level;
                    result.Connected = true;
                    result.Detail = "四档电量";
                    return result;
                }
            }

            result.Connected = false;
            return result;
        }

        private static int VoltageToPercent(int millivolts)
        {
            // 4.20V -> 100%，3.30V -> 0% 的线性近似
            int percent = (int)Math.Round((millivolts - 3300) * 100.0 / 900.0);
            return Math.Max(1, Math.Min(100, percent));
        }

        /// <summary>枚举接收器上真正应答的配对设备（index 1..6，以及 0xFF 接收器自身）。</summary>
        public static List<byte> ProbeDeviceIndexes(HidppSession session, out bool anyProtocol)
        {
            List<byte> list = new List<byte>();
            anyProtocol = false;
            for (byte index = 1; index <= 6; index++)
            {
                int major = ProtocolMajor(session, index);
                if (major > 0)
                {
                    anyProtocol = true;
                    list.Add(index);
                }
            }
            if (list.Count == 0)
            {
                int major = ProtocolMajor(session, 0xFF);
                if (major > 0) { anyProtocol = true; list.Add(0xFF); }
            }
            return list;
        }

        /// <summary>HID++ 1.0 设备探测：用寄存器 0x0D 逐个 index 试。</summary>
        public static List<byte> ProbeLegacyIndexes(HidppSession session)
        {
            List<byte> list = new List<byte>();
            for (byte index = 1; index <= 6; index++)
            {
                byte[] reply = session.Register(index, 0x81, 0x0D, null, 160);
                if (reply != null && reply.Length >= 6 && reply[2] == 0x81 && reply[3] == 0x0D) list.Add(index);
            }
            if (list.Count == 0)
            {
                byte[] reply = session.Register(0xFF, 0x81, 0x0D, null, 160);
                if (reply != null && reply.Length >= 6 && reply[2] == 0x81 && reply[3] == 0x0D) list.Add(0xFF);
            }
            return list;
        }

        /// <summary>解析 "046d:c547#1" 形式的地址。</summary>
        public static bool ParseAddress(string address, out string vidPid, out int index)
        {
            vidPid = null;
            index = 1;
            if (string.IsNullOrEmpty(address)) return false;
            int hash = address.IndexOf('#');
            if (hash <= 0) return false;
            vidPid = address.Substring(0, hash);
            int parsed;
            if (!int.TryParse(address.Substring(hash + 1), out parsed)) return false;
            index = parsed;
            return true;
        }

        public static string BuildAddress(string vidPid, int index)
        {
            return vidPid + "#" + index.ToString();
        }

        private static bool _ghubChecked;
        private static bool _ghubRunning;
        private static DateTime _ghubCheckTime = DateTime.MinValue;

        /// <summary>
        /// 是否有罗技官方软件在跑（G HUB / Logi Options+）。
        /// 它们也在轮询同一个 HID++ 通道，双方同时发请求会互相打断应答，
        /// 所以检测到它们时程序会自动进入「低干扰模式」（大幅拉长轮询间隔、不做唤醒重试）。
        /// </summary>
        public static bool LogitechSoftwareRunning()
        {
            if (_ghubChecked && (DateTime.Now - _ghubCheckTime).TotalSeconds < 30) return _ghubRunning;
            _ghubChecked = true;
            _ghubCheckTime = DateTime.Now;
            string[] names = new string[]
            {
                "lghub", "lghub_agent", "lghub_updater",
                "LogiOptionsPlus", "logioptionsplus_agent",
                "LogiOptions", "LogiPluginService", "SetPoint"
            };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    if (System.Diagnostics.Process.GetProcessesByName(names[i]).Length > 0)
                    {
                        _ghubRunning = true;
                        return true;
                    }
                }
                catch { }
            }
            _ghubRunning = false;
            return false;
        }
    }
}
