using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Jus
{
    /// <summary>Une collection HID vue par Windows (un appareil peut en exposer plusieurs).</summary>
    sealed class HidInfo
    {
        public string Path, Product;
        public ushort Vid, Pid, UsagePage, Usage;
        public int InputLen, OutputLen, FeatureLen;
        /// <summary>Manette virtuelle (ViGEm, utilisé par DS4Windows) : copie d'une vraie manette, déjà comptée.</summary>
        public bool IsVirtual;
        public bool IsBluetooth => Path.IndexOf("{00001124-", StringComparison.OrdinalIgnoreCase) >= 0
                                || Path.IndexOf("bth", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static class Hid
    {
        public static List<HidInfo> Enumerate()
        {
            var result = new List<HidInfo>();
            Native.HidD_GetHidGuid(out Guid guid);
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return result;
            try
            {
                var data = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA)) };
                for (int i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
                {
                    var dev = new Native.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(Native.SP_DEVINFO_DATA)) };
                    string path = InterfacePath(set, ref data, ref dev);
                    if (path == null) continue;
                    HidInfo info = Describe(path);
                    if (info == null) continue;
                    if (info.UsagePage == 1 && (info.Usage == 4 || info.Usage == 5)) info.IsVirtual = OnVirtualBus(dev.DevInst);
                    result.Add(info);
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        static string InterfacePath(IntPtr set, ref Native.SP_DEVICE_INTERFACE_DATA data, ref Native.SP_DEVINFO_DATA dev)
        {
            Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
            if (size <= 0) return null;
            IntPtr mem = Marshal.AllocHGlobal(size);
            IntPtr devMem = Marshal.AllocHGlobal(dev.cbSize);
            try
            {
                Marshal.WriteInt32(mem, IntPtr.Size == 8 ? 8 : 6);
                Marshal.StructureToPtr(dev, devMem, false);
                if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref data, mem, size, out _, devMem)) return null;
                dev = (Native.SP_DEVINFO_DATA)Marshal.PtrToStructure(devMem, typeof(Native.SP_DEVINFO_DATA));
                return Marshal.PtrToStringUni(mem + 4);
            }
            finally { Marshal.FreeHGlobal(mem); Marshal.FreeHGlobal(devMem); }
        }

        /// <summary>Vrai si l'appareil descend du bus virtuel ViGEm (manettes Xbox 360 / DS4 créées par DS4Windows et d'autres).</summary>
        static bool OnVirtualBus(uint devInst)
        {
            var buffer = new byte[1024];
            for (int depth = 0; depth < 8 && devInst != 0; depth++)
            {
                int length = buffer.Length;
                if (Native.CM_Get_DevNode_Registry_Property(devInst, Native.CM_DRP_HARDWAREID, out _, buffer, ref length, 0) == Native.CR_SUCCESS)
                {
                    string ids = Encoding.Unicode.GetString(buffer, 0, Math.Max(0, length));
                    if (ids.IndexOf("ViGEmBus", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                if (Native.CM_Get_Parent(out uint parent, devInst, 0) != Native.CR_SUCCESS) break;
                devInst = parent;
            }
            return false;
        }

        static HidInfo Describe(string path)
        {
            // Accès 0 : suffit pour lire la description, même pour les claviers et souris réservés par Windows.
            using (SafeFileHandle h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var attr = new Native.HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES)) };
                if (!Native.HidD_GetAttributes(h, ref attr)) return null;
                if (!Native.HidD_GetPreparsedData(h, out IntPtr pre)) return null;
                try
                {
                    if (Native.HidP_GetCaps(pre, out Native.HIDP_CAPS caps) != Native.HIDP_STATUS_SUCCESS) return null;
                    var sb = new StringBuilder(128);
                    string product = Native.HidD_GetProductString(h, sb, sb.Capacity * 2) ? sb.ToString().Trim() : "";
                    return new HidInfo
                    {
                        Path = path, Product = product, Vid = attr.VendorID, Pid = attr.ProductID,
                        UsagePage = caps.UsagePage, Usage = caps.Usage,
                        InputLen = caps.InputReportByteLength, OutputLen = caps.OutputReportByteLength, FeatureLen = caps.FeatureReportByteLength,
                    };
                }
                finally { Native.HidD_FreePreparsedData(pre); }
            }
        }
    }

    /// <summary>Une collection HID ouverte, avec lectures et écritures à délai limité.</summary>
    sealed class HidDevice : IDisposable
    {
        public readonly HidInfo Info;
        public readonly SafeFileHandle Handle;

        HidDevice(HidInfo info, SafeFileHandle handle) { Info = info; Handle = handle; }

        public static HidDevice Open(HidInfo info, bool readWrite = true)
        {
            uint access = readWrite ? Native.GENERIC_READ | Native.GENERIC_WRITE : 0;
            SafeFileHandle h = Native.CreateFile(info.Path, access, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            return new HidDevice(info, h);
        }

        public void Flush() => Native.HidD_FlushQueue(Handle);
        public bool Write(byte[] data, int timeoutMs = 300) => Transfer(true, data, data.Length, timeoutMs) == data.Length;
        public int Read(byte[] buffer, int timeoutMs) => Transfer(false, buffer, buffer.Length, timeoutMs);
        public bool GetFeature(byte[] buffer) => Native.HidD_GetFeature(Handle, buffer, buffer.Length);
        public bool SetFeature(byte[] buffer) => Native.HidD_SetFeature(Handle, buffer, buffer.Length);

        int Transfer(bool write, byte[] buffer, int length, int timeoutMs)
        {
            if (length <= 0) return -1;
            using (var done = new ManualResetEvent(false))
            {
                IntPtr ov = Marshal.AllocHGlobal(Native.OverlappedSize);
                GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    Marshal.StructureToPtr(new NativeOverlapped { EventHandle = done.SafeWaitHandle.DangerousGetHandle() }, ov, false);
                    IntPtr p = pin.AddrOfPinnedObject();
                    bool ok = write ? Native.WriteFile(Handle, p, length, out _, ov) : Native.ReadFile(Handle, p, length, out _, ov);
                    if (!ok && Marshal.GetLastWin32Error() != Native.ERROR_IO_PENDING) return -1;
                    if (!done.WaitOne(timeoutMs))
                    {
                        Native.CancelIoEx(Handle, ov);
                        Native.GetOverlappedResult(Handle, ov, out _, true); // attendre l'annulation avant de libérer le tampon
                        return 0;
                    }
                    return Native.GetOverlappedResult(Handle, ov, out int n, false) ? n : -1;
                }
                finally { pin.Free(); Marshal.FreeHGlobal(ov); }
            }
        }

        public void Dispose() => Handle.Dispose();
    }
}
