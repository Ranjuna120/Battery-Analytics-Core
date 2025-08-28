using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryAnalytics.Core;

// Low-level Windows Battery Class Driver interop definitions
internal static class BatteryInterop
{
    // GUIDs
    public static readonly Guid GUID_DEVCLASS_BATTERY = new("72631e54-78a4-11d0-bcf6-00a0c9081ff6");
    public static readonly Guid GUID_DEVINTERFACE_BATTERY = new("72631e55-78a4-11d0-bcf6-00a0c9081ff6");

    // SetupDi constants
    private const int DIGCF_DEFAULT = 0x00000001;
    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_ALLCLASSES = 0x00000004;
    private const int DIGCF_PROFILE = 0x00000008;
    private const int DIGCF_DEVICEINTERFACE = 0x00000010;

    // File access
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;

    // Battery IOCTLs (from batclass.h)
    public const uint IOCTL_BATTERY_QUERY_TAG = 0x00294044;
    public const uint IOCTL_BATTERY_QUERY_INFORMATION = 0x00294048;
    public const uint IOCTL_BATTERY_QUERY_STATUS = 0x0029404c; // correct value; set-information (0x00294050) not needed

    // Battery info levels
    public enum BATTERY_QUERY_INFO_LEVEL : int
    {
        BatteryInformation = 0,
        BatteryManufactureDate = 1,
        BatteryManufactureName = 2,
        BatteryUniqueID = 3,
        BatterySerialNumber = 4,
        BatteryDeviceName = 5,
        BatteryChemistry = 6,
        BatteryTemperature = 7,
        BatteryFullChargedCapacity = 8,
        BatteryCycleCount = 9,
    }

    [Flags]
    public enum BatteryPowerState : uint
    {
        BATTERY_POWER_ON_LINE = 0x00000001,
        BATTERY_DISCHARGING = 0x00000002,
        BATTERY_CHARGING = 0x00000004,
        BATTERY_CRITICAL = 0x00000008
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_QUERY_INFORMATION
    {
        public uint BatteryTag;
        public BATTERY_QUERY_INFO_LEVEL InformationLevel;
        public int AtRate; // ignored unless BatteryEstimatedTime
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_INFORMATION
    {
        public uint Capabilities;
        [MarshalAs(UnmanagedType.U1)] public bool Technology; // 0=Primary, 1=Rechargeable
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] Chemistry; // Four-char code
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;
        public uint CycleCount; // NOTE: doc sometimes shows UCHAR, modern returns ULONG
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_WAIT_STATUS
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_STATUS
    {
        public uint PowerState;
        public uint Capacity; // remaining capacity (mWh)
        public uint Voltage;  // mV
        public int Rate;      // mW (negative sometimes means discharging)
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, byte[] deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, ref uint lpInBuffer, int nInBufferSize, out uint lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize, byte[] lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, ref BATTERY_QUERY_INFORMATION inBuf, int inBufSize, byte[] outBuf, int outBufSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, ref BATTERY_WAIT_STATUS inBuf, int inBufSize, ref BATTERY_STATUS outBuf, int outBufSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public record BatteryStaticInfo(
        string DevicePath,
        string? Manufacturer,
        string? SerialNumber,
        string? DeviceName,
        string? Chemistry,
        uint DesignedCapacity,
        uint FullChargedCapacity,
        uint? CycleCount,
        DateOnly? ManufactureDate
    );

    public record BatteryDynamicStatus(
        DateTime TimestampUtc,
        string DevicePath,
        uint RemainingCapacity,
        int Rate,
        uint Voltage,
        BatteryPowerState PowerState
    )
    {
        public double? EstimatedSecondsToEmpty
        {
            get
            {
                if (Rate < 0) // discharging
                {
                    var rateAbs = Math.Abs(Rate);
                    if (rateAbs > 0)
                        return RemainingCapacity / (double)rateAbs * 3600.0; // mWh / (mW) * 3600 sec
                }
                return null;
            }
        }
    }

    public sealed class BatteryDevice : IDisposable
    {
        public string DevicePath { get; }
        public uint Tag { get; private set; }
        private IntPtr _handle;

        internal BatteryDevice(string devicePath, IntPtr handle)
        {
            DevicePath = devicePath;
            _handle = handle;
            Tag = QueryTag();
        }

        private uint QueryTag()
        {
            uint tagInput = 0; // ignored
            uint tag;
            int br;
            if (!DeviceIoControl(_handle, IOCTL_BATTERY_QUERY_TAG, ref tagInput, sizeof(uint), out tag, sizeof(uint), out br, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to query battery tag");
            return tag;
        }

        private byte[] QueryRaw(BATTERY_QUERY_INFO_LEVEL level)
        {
            var q = new BATTERY_QUERY_INFORMATION { BatteryTag = Tag, InformationLevel = level, AtRate = 0 };
            var buf = new byte[256];
            if (!DeviceIoControl(_handle, IOCTL_BATTERY_QUERY_INFORMATION, ref q, Marshal.SizeOf<BATTERY_QUERY_INFORMATION>(), buf, buf.Length, out var br, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Query info level {level} failed");
            Array.Resize(ref buf, br);
            return buf;
        }

        public BatteryStaticInfo GetStaticInfo()
        {
            BATTERY_INFORMATION battInfo;
            {
                var raw = QueryRaw(BATTERY_QUERY_INFO_LEVEL.BatteryInformation);
                GCHandle h = GCHandle.Alloc(raw, GCHandleType.Pinned);
                try { battInfo = Marshal.PtrToStructure<BATTERY_INFORMATION>(h.AddrOfPinnedObject()); }
                finally { h.Free(); }
            }

            string? chemistry = null;
            if (battInfo.Chemistry is { Length: 4 })
            {
                chemistry = Encoding.ASCII.GetString(battInfo.Chemistry).TrimEnd('\0', ' ');
            }

            string? manufacturer = TryGetString(BATTERY_QUERY_INFO_LEVEL.BatteryManufactureName);
            string? serial = TryGetString(BATTERY_QUERY_INFO_LEVEL.BatterySerialNumber);
            string? deviceName = TryGetString(BATTERY_QUERY_INFO_LEVEL.BatteryDeviceName);

            DateOnly? mfgDate = null;
            try
            {
                var rawDate = QueryRaw(BATTERY_QUERY_INFO_LEVEL.BatteryManufactureDate);
                if (rawDate.Length >= 3 * sizeof(ushort))
                {
                    ushort y = BitConverter.ToUInt16(rawDate, 0);
                    ushort m = BitConverter.ToUInt16(rawDate, 2);
                    ushort d = BitConverter.ToUInt16(rawDate, 4);
                    if (y > 1990 && m is > 0 and <= 12 && d is > 0 and <= 31)
                        mfgDate = new DateOnly(y, m, d);
                }
            }
            catch { }

            uint? cycleCount = null;
            try
            {
                var rawCycle = QueryRaw(BATTERY_QUERY_INFO_LEVEL.BatteryCycleCount);
                if (rawCycle.Length >= 4)
                    cycleCount = BitConverter.ToUInt32(rawCycle, 0);
            }
            catch { }

            return new BatteryStaticInfo(DevicePath, manufacturer, serial, deviceName, chemistry, battInfo.DesignedCapacity, battInfo.FullChargedCapacity, cycleCount, mfgDate);
        }

        private string? TryGetString(BATTERY_QUERY_INFO_LEVEL lvl)
        {
            try
            {
                var raw = QueryRaw(lvl);
                if (raw.Length == 0) return null;
                var str = Encoding.Unicode.GetString(raw).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(str))
                    str = Encoding.ASCII.GetString(raw).TrimEnd('\0');
                return str.Trim();
            }
            catch { return null; }
        }

        public double? GetTemperatureCelsius()
        {
            try
            {
                var raw = QueryRaw(BATTERY_QUERY_INFO_LEVEL.BatteryTemperature);
                if (raw.Length >= 4)
                {
                    int deciKelvin = BitConverter.ToInt32(raw, 0);
                    return (deciKelvin / 10.0) - 273.15;
                }
            }
            catch { }
            return null;
        }

        public BatteryDynamicStatus GetStatus()
        {
            var wait = new BATTERY_WAIT_STATUS { BatteryTag = Tag, Timeout = 0, LowCapacity = 0, HighCapacity = 0 };
            var status = new BATTERY_STATUS();
            if (!DeviceIoControl(_handle, IOCTL_BATTERY_QUERY_STATUS, ref wait, Marshal.SizeOf<BATTERY_WAIT_STATUS>(), ref status, Marshal.SizeOf<BATTERY_STATUS>(), out var br, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Query status failed");
            return new BatteryDynamicStatus(DateTime.UtcNow, DevicePath, status.Capacity, status.Rate, status.Voltage, (BatteryPowerState)status.PowerState);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    public static BatteryDevice[] EnumerateBatteries()
    {
        var list = new System.Collections.Generic.List<BatteryDevice>();
        IntPtr h = IntPtr.Zero;
        try
        {
            var guid = GUID_DEVINTERFACE_BATTERY; // local copy because P/Invoke requires ref
            h = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_DEVICEINTERFACE | DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (h == IntPtr.Zero || h.ToInt64() == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed");

            int index = 0;
            while (true)
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                var enumGuid = GUID_DEVINTERFACE_BATTERY;
                if (!SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref enumGuid, index, ref ifData))
                {
                    int err = Marshal.GetLastWin32Error();
                    const int ERROR_NO_MORE_ITEMS = 259;
                    if (err == ERROR_NO_MORE_ITEMS) break;
                    throw new Win32Exception(err, "SetupDiEnumDeviceInterfaces failed");
                }

                // Query required size
                if (!SetupDiGetDeviceInterfaceDetail(h, ref ifData, IntPtr.Zero, 0, out int required, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    const int ERROR_INSUFFICIENT_BUFFER = 122;
                    if (err != ERROR_INSUFFICIENT_BUFFER)
                        throw new Win32Exception(err, "SetupDiGetDeviceInterfaceDetail (size) failed");
                }

                var detailData = new byte[required];
                // cbSize depends on architecture: On 64-bit, 8; on 32-bit, 6 (size of DWORD + TCHAR)
                int cbSize = IntPtr.Size == 8 ? 8 : 6;
                BitConverter.GetBytes(cbSize).CopyTo(detailData, 0);

                if (!SetupDiGetDeviceInterfaceDetail(h, ref ifData, detailData, detailData.Length, out required, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetDeviceInterfaceDetail failed");

                // Device path is at offset 4 (32-bit) or 8 (64-bit)
                int pathOffset = (IntPtr.Size == 8) ? 8 : 4;
                string devicePath = Marshal.PtrToStringAuto(Marshal.UnsafeAddrOfPinnedArrayElement(detailData, pathOffset)) ?? string.Empty;

                var handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                {
                    index++;
                    continue; // skip if can't open
                }
                try
                {
                    var dev = new BatteryDevice(devicePath, handle);
                    list.Add(dev);
                }
                catch
                {
                    CloseHandle(handle);
                }

                index++;
            }
        }
        finally
        {
            if (h != IntPtr.Zero && h.ToInt64() != -1)
                SetupDiDestroyDeviceInfoList(h);
        }
        return list.ToArray();
    }
}
