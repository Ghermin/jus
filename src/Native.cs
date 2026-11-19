using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Jus
{
    /// <summary>Appels Windows utilisés par l'appli (HID, SetupAPI, fenêtres).</summary>
    static class Native
    {
        // ---------------------------------------------------------------- fichiers / E-S
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int ERROR_IO_PENDING = 997;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int size, out int read, IntPtr overlapped);
        [DllImport("kernel32", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int size, out int written, IntPtr overlapped);
        [DllImport("kernel32", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out int transferred, bool wait);
        [DllImport("kernel32", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }
        [DllImport("kernel32")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        // ---------------------------------------------------------------- HID
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        public const int HIDP_STATUS_SUCCESS = 0x00110000;
        public const int HidP_Input = 0, HidP_Feature = 2;

        [DllImport("hid")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid")] public static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("hid")] public static extern int HidP_GetValueCaps(int type, byte[] caps, ref ushort length, IntPtr data);
        [DllImport("hid")] public static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr data, byte[] report, int length);
        [DllImport("hid", CharSet = CharSet.Unicode)] public static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder s, int bytes);
        [DllImport("hid", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid", SetLastError = true)] public static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid")] public static extern bool HidD_FlushQueue(SafeFileHandle h);

        // ---------------------------------------------------------------- SetupAPI
        public const int DIGCF_PRESENT = 2, DIGCF_ALLCLASSES = 4, DIGCF_DEVICEINTERFACE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY
        {
            public Guid fmtid; public uint pid;
            public DEVPROPKEY(string guid, uint id) { fmtid = new Guid(guid); pid = id; }
        }

        [DllImport("setupapi", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid cls, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(IntPtr cls, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid cls, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA info);
        [DllImport("setupapi", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
        public static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA info, StringBuilder id, int size, out int required);
        [DllImport("setupapi", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
        public static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key, out uint type, byte[] buffer, int size, out int required, int flags);
        [DllImport("setupapi")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        // ---------------------------------------------------------------- Bluetooth classique
        [StructLayout(LayoutKind.Sequential)]
        public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            public bool fReturnAuthenticated, fReturnRemembered, fReturnUnknown, fReturnConnected, fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }
        [StructLayout(LayoutKind.Sequential)] public struct SYSTEMTIME { public short Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            public bool fConnected, fRemembered, fAuthenticated;
            public SYSTEMTIME stLastSeen, stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
        }
        [DllImport("BluetoothApis.dll", SetLastError = true)] public static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS p, ref BLUETOOTH_DEVICE_INFO info);
        [DllImport("BluetoothApis.dll", SetLastError = true)] public static extern bool BluetoothFindNextDevice(IntPtr find, ref BLUETOOTH_DEVICE_INFO info);
        [DllImport("BluetoothApis.dll")] public static extern bool BluetoothFindDeviceClose(IntPtr find);

        // ---------------------------------------------------------------- XInput
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_BATTERY_INFORMATION { public byte BatteryType, BatteryLevel; }
        [DllImport("xinput1_4", EntryPoint = "XInputGetBatteryInformation")]
        public static extern int XInputGetBatteryInformation(int user, byte devType, out XINPUT_BATTERY_INFORMATION info);

        // ---------------------------------------------------------------- fenêtres
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int W, H; public SIZE(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT p, int count);
        [DllImport("user32")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
        [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32")] public static extern short GetAsyncKeyState(int key);
        [StructLayout(LayoutKind.Sequential)]
        public struct TRACKMOUSEEVENT { public int cbSize, dwFlags; public IntPtr hwndTrack; public int dwHoverTime; }
        [DllImport("user32")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
        [DllImport("user32", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr ptDst, ref SIZE size, IntPtr src, ref POINT ptSrc, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("kernel32")] public static extern bool AttachConsole(int pid);

        /// <summary>Taille de NativeOverlapped, pour l'allouer hors du tas géré.</summary>
        public static readonly int OverlappedSize = Marshal.SizeOf(typeof(NativeOverlapped));
    }
}
