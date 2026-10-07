using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ControllerOS.Windows;

/// <summary>Enumerates currently-present HID interfaces and their public HID capabilities.</summary>
public sealed class WindowsHidDeviceEnumerator : IWindowsDeviceEnumerator
{
    private const uint DevicePresent = 0x00000002;
    private const uint DeviceInterface = 0x00000010;
    private const int ErrorNoMoreItems = 259;
    private const uint OpenExisting = 3;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint FriendlyNameProperty = 0x0000000C;

    public IReadOnlyList<WindowsHidDevice> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows HID discovery is available only on Windows.");

        Native.HidD_GetHidGuid(out Guid hidGuid);
        using SafeDeviceInfoSetHandle deviceSet = Native.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DevicePresent | DeviceInterface);
        if (deviceSet.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enumerate HID interfaces.");

        var devices = new List<WindowsHidDevice>();
        for (uint index = 0; ; index++)
        {
            var interfaceData = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
            if (!Native.SetupDiEnumDeviceInterfaces(deviceSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreItems)
                    break;
                throw new Win32Exception(error, "Windows failed while enumerating HID interfaces.");
            }

            var deviceInfo = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
            _ = Native.SetupDiGetDeviceInterfaceDetail(deviceSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, ref deviceInfo);
            int detailError = Marshal.GetLastWin32Error();
            if (requiredSize == 0 || detailError != 122)
                continue;

            IntPtr detailBuffer = Marshal.AllocHGlobal(checked((int)requiredSize));
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA_W has a 4-byte path offset;
                // cbSize is 8 on 64-bit and 6 on 32-bit Windows.
                Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                deviceInfo = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
                if (!Native.SetupDiGetDeviceInterfaceDetail(deviceSet, ref interfaceData, detailBuffer, requiredSize, out _, ref deviceInfo))
                    continue;

                string? devicePath = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
                if (string.IsNullOrWhiteSpace(devicePath))
                    continue;

                devices.Add(ReadDevice(devicePath, deviceSet, ref deviceInfo));
            }
            finally
            {
                Marshal.FreeHGlobal(detailBuffer);
            }
        }

        return devices.OrderBy(device => device.Product, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.VendorId)
            .ThenBy(device => device.ProductId)
            .ThenBy(device => device.InterfaceNumber)
            .ToArray();
    }

    private static WindowsHidDevice ReadDevice(string devicePath, SafeDeviceInfoSetHandle deviceSet, ref DeviceInfoData deviceInfo)
    {
        string? instanceId = GetInstanceId(deviceSet, ref deviceInfo);
        using SafeFileHandle handle = Native.CreateFile(devicePath, 0, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        HidAttributes? attributes = null;
        HidCapabilities? capabilities = null;
        string? manufacturer = null;
        string? product = null;
        if (!handle.IsInvalid)
        {
            var nativeAttributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
            if (Native.HidD_GetAttributes(handle, ref nativeAttributes))
                attributes = nativeAttributes;

            manufacturer = GetHidString(handle, Native.HidD_GetManufacturerString);
            product = GetHidString(handle, Native.HidD_GetProductString);
            capabilities = ReadCapabilities(handle);
        }

        string hardwareId = instanceId ?? devicePath;
        return new WindowsHidDevice(
            devicePath,
            manufacturer,
            product ?? GetDeviceProperty(deviceSet, ref deviceInfo, FriendlyNameProperty),
            Nonzero(attributes?.VendorId) ?? ParseHardwareId(hardwareId, "VID_"),
            Nonzero(attributes?.ProductId) ?? ParseHardwareId(hardwareId, "PID_"),
            Nonzero(attributes?.VersionNumber) ?? ParseHardwareId(hardwareId, "REV_"),
            ParseHardwareId(hardwareId, "MI_"),
            capabilities?.UsagePage,
            capabilities?.Usage,
            capabilities?.InputReportByteLength,
            capabilities?.OutputReportByteLength,
            capabilities?.NumberInputButtonCaps,
            capabilities?.NumberInputValueCaps);
    }

    private static string? GetInstanceId(SafeDeviceInfoSetHandle deviceSet, ref DeviceInfoData deviceInfo)
    {
        var result = new StringBuilder(512);
        return Native.SetupDiGetDeviceInstanceId(deviceSet, ref deviceInfo, result, (uint)result.Capacity, out _)
            ? result.ToString()
            : null;
    }

    private static string? GetDeviceProperty(SafeDeviceInfoSetHandle deviceSet, ref DeviceInfoData deviceInfo, uint property)
    {
        var result = new StringBuilder(512);
        return Native.SetupDiGetDeviceRegistryProperty(deviceSet, ref deviceInfo, property, out _, result, (uint)(result.Capacity * sizeof(char)), out _)
            ? result.ToString()
            : null;
    }

    private static string? GetHidString(SafeFileHandle handle, HidStringGetter getString)
    {
        byte[] buffer = new byte[512];
        if (!getString(handle, buffer, (uint)buffer.Length))
            return null;

        string value = Encoding.Unicode.GetString(buffer);
        int terminator = value.IndexOf('\0');
        return (terminator < 0 ? value : value[..terminator]).Trim();
    }

    private static HidCapabilities? ReadCapabilities(SafeFileHandle handle)
    {
        if (!Native.HidD_GetPreparsedData(handle, out IntPtr preparsedData))
            return null;

        try
        {
            int status = Native.HidP_GetCaps(preparsedData, out HidCapabilities capabilities);
            return status >= 0 ? capabilities : null;
        }
        finally
        {
            _ = Native.HidD_FreePreparsedData(preparsedData);
        }
    }

    private static ushort? Nonzero(ushort? value) => value is > 0 ? value : null;

    private static ushort? ParseHardwareId(string value, string prefix)
    {
        int start = value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || start + prefix.Length + 4 > value.Length)
            return null;

        return ushort.TryParse(value.AsSpan(start + prefix.Length, 4), System.Globalization.NumberStyles.HexNumber, null, out ushort parsed)
            ? parsed
            : null;
    }

    private delegate bool HidStringGetter(SafeFileHandle handle, byte[] buffer, uint bufferLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public int Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
        public ushort Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCapabilities
    {
        public ushort UsagePage;
        public ushort Usage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    private sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeDeviceInfoSetHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => Native.SetupDiDestroyDeviceInfoList(handle);
    }

    private static class Native
    {
        [DllImport("hid.dll", ExactSpelling = true)]
        internal static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiEnumDeviceInterfaces", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(SafeDeviceInfoSetHandle deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(SafeDeviceInfoSetHandle deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, ref DeviceInfoData deviceInfoData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInstanceId(SafeDeviceInfoSetHandle deviceInfoSet, ref DeviceInfoData deviceInfoData, StringBuilder deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceRegistryProperty(SafeDeviceInfoSetHandle deviceInfoSet, ref DeviceInfoData deviceInfoData, uint property, out uint propertyRegDataType, StringBuilder propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("hid.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetAttributes(SafeFileHandle device, ref HidAttributes attributes);

        [DllImport("hid.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetManufacturerString(SafeFileHandle device, [Out] byte[] buffer, uint bufferLength);

        [DllImport("hid.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetProductString(SafeFileHandle device, [Out] byte[] buffer, uint bufferLength);

        [DllImport("hid.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

        [DllImport("hid.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", ExactSpelling = true)]
        internal static extern int HidP_GetCaps(IntPtr preparsedData, out HidCapabilities capabilities);
    }
}
