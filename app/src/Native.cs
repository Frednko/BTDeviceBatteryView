using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BatteryBar
{
    /// <summary>Win32 / SetupAPI / 蓝牙 GATT 的原生互操作。</summary>
    internal static class Native
    {
        // ---------- user32 / dwmapi ----------
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern int GetDpiForWindow(IntPtr hWnd);
        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        /// <summary>距最后一次键鼠输入过了多少毫秒（用于判断用户是否刚动过设备）。</summary>
        public static int IdleMilliseconds()
        {
            try
            {
                LASTINPUTINFO info = new LASTINPUTINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref info)) return int.MaxValue;
                uint tick = (uint)Environment.TickCount;
                return (int)(tick - info.dwTime);
            }
            catch { return int.MaxValue; }
        }

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 0x2;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// <summary>Win11 圆角（不支持时静默忽略）。</summary>
        public static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
                int dark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch { }
        }

        public static void DragWindow(IntPtr hwnd)
        {
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        // ---------- kernel32 ----------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_RW = 0x3;
        private const uint OPEN_EXISTING = 3;

        public static SafeFileHandle OpenDevice(string path, bool write)
        {
            return CreateFile(path, write ? (GENERIC_READ | GENERIC_WRITE) : GENERIC_READ, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        // ---------- setupapi ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int detailSize, out int requiredSize, IntPtr deviceInfoData);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        public const int DIGCF_PRESENT = 0x2;
        public const int DIGCF_DEVICEINTERFACE = 0x10;

        /// <summary>枚举某个设备接口类的全部实例路径。</summary>
        public static List<string> EnumDeviceInterfaces(Guid classGuid)
        {
            List<string> list = new List<string>();
            IntPtr set = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == (IntPtr)(-1)) return list;
            try
            {
                SP_DEVICE_INTERFACE_DATA did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                for (int i = 0; ; i++)
                {
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref classGuid, i, ref did)) break;
                    int needed;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out needed, IntPtr.Zero);
                    if (needed <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(needed + 8);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, buf, needed, out needed, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                        if (!string.IsNullOrEmpty(path)) list.Add(path);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        // ---------- cfgmgr32：接口属性 ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        public static readonly DEVPROPKEY PKEY_Device_InstanceId = MakeKey("78C34FC8-104A-4ACA-9EA4-524D52996E57", 1);
        public static readonly DEVPROPKEY PKEY_Device_FriendlyName = MakeKey("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);
        public static readonly DEVPROPKEY PKEY_Device_DeviceDesc = MakeKey("A45C254E-DF1C-4EFD-8020-67D146A850E0", 2);

        private static DEVPROPKEY MakeKey(string guid, uint pid)
        {
            DEVPROPKEY k = new DEVPROPKEY();
            k.fmtid = new Guid(guid);
            k.pid = pid;
            return k;
        }

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_Interface_PropertyW(string deviceInterface, ref DEVPROPKEY propertyKey, out uint propertyType, IntPtr buffer, ref uint size, uint flags);

        public static string GetInterfaceStringProperty(string interfacePath, DEVPROPKEY key)
        {
            uint type;
            uint size = 0;
            IntPtr buf = IntPtr.Zero;
            try
            {
                int cr = CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out type, IntPtr.Zero, ref size, 0);
                if (size == 0 || size > 4096) return null;
                buf = Marshal.AllocHGlobal((int)size);
                cr = CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out type, buf, ref size, 0);
                if (cr != 0) return null;
                return Marshal.PtrToStringUni(buf);
            }
            catch { return null; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        // ---------- HID ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
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
        public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")]
        public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attrs);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder buffer, int len);
        [DllImport("hid.dll")]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);

        // ---------- 蓝牙：原生 GATT 客户端 ----------
        public static readonly Guid BleDeviceInterface = new Guid("781AEE18-7733-4CE4-ADD4-91CED088D8C3");
        public static readonly Guid BatteryServiceUuid = new Guid("0000180F-0000-1000-8000-00805F9B34FB");
        public static readonly Guid BatteryLevelCharUuid = new Guid("00002A19-0000-1000-8000-00805F9B34FB");

        [StructLayout(LayoutKind.Explicit, Size = 20)]
        public struct BTH_LE_UUID
        {
            [FieldOffset(0)] public byte IsShortUuid;
            [FieldOffset(4)] public ushort ShortUuid;
            [FieldOffset(4)] public Guid LongUuid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BTH_LE_GATT_SERVICE
        {
            public BTH_LE_UUID ServiceUuid;
            public ushort AttributeHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BTH_LE_GATT_CHARACTERISTIC
        {
            public ushort ServiceHandle;
            public BTH_LE_UUID CharacteristicUuid;
            public ushort AttributeHandle;
            public ushort CharacteristicValueHandle;
            public byte IsBroadcast;
            public byte IsReadable;
            public byte IsWritableWithoutResponse;
            public byte IsWritable;
            public byte IsNotifiable;
            public byte IsIndicatable;
            public byte IsSignedWritable;
            public byte IsExtendedProperties;
            public byte HasExtendedProperties;
        }

        public const uint BLUETOOTH_GATT_FLAG_NONE = 0;

        [DllImport("bluetoothapis.dll", SetLastError = true)]
        public static extern int BluetoothGATTGetServices(IntPtr hDevice, ref ushort serviceCount, IntPtr services, uint flags);
        [DllImport("bluetoothapis.dll", SetLastError = true)]
        public static extern int BluetoothGATTGetCharacteristics(IntPtr hDevice, ref BTH_LE_GATT_SERVICE service, ref ushort characteristicCount, IntPtr characteristics, uint flags);
        [DllImport("bluetoothapis.dll", SetLastError = true)]
        public static extern int BluetoothGATTGetCharacteristicValue(IntPtr hDevice, ref BTH_LE_GATT_CHARACTERISTIC characteristic, uint bufferSize, IntPtr buffer, out uint needed, uint flags);
    }
}
