using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Devices;
using Microsoft.Win32.SafeHandles;

namespace ControllerOS.Windows;

public sealed partial class WindowsHidDeviceEnumerator
{
    private const uint GenericRead = 0x80000000;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int HidpStatusSuccess = 0x00110000;
    private const int HidpStatusIncompatibleReportId = unchecked((int)0xC011000A);
    private const int HidpStatusNull = unchecked((int)0x80110001);

    /// <summary>Opens only the selected enumerated interface for asynchronous raw input reads.</summary>
    public WindowsHidInputCapture OpenInputCapture(WindowsHidDevice device, string? retailModelName = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows HID capture is available only on Windows.");
        if (string.IsNullOrWhiteSpace(device.DevicePath) || !device.DevicePath.StartsWith("\\\\?\\HID#", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Capture requires a selected HID interface returned by Windows enumeration.", nameof(device));

        SafeFileHandle handle = Native.CreateFile(device.DevicePath, GenericRead, 0x00000001 | 0x00000002, IntPtr.Zero, 3, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Windows could not open the selected HID interface for input.");
        }

        if (!Native.HidD_GetPreparsedData(handle, out IntPtr preparsedData))
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the selected HID report descriptor.");
        }

        try
        {
            int status = Native.HidP_GetCaps(preparsedData, out HidCapabilities capabilities);
            if (status != HidpStatusSuccess)
                throw new InvalidOperationException("Windows HID parser rejected the selected interface capabilities.");
            if (capabilities.InputReportByteLength is 0 or > RawHidInputPipeline.MaximumReportBytes)
                throw new InvalidOperationException($"Selected HID input report size must be 1..{RawHidInputPipeline.MaximumReportBytes} bytes.");
            if (device.VendorId is null or 0 || device.ProductId is null or 0 || capabilities.UsagePage == 0 || capabilities.Usage == 0)
                throw new InvalidOperationException("Selected HID interface does not expose the stable identifiers required for a safe mapping.");

            var decoder = new WindowsHidReportDecoder(preparsedData, capabilities, device, retailModelName);
            var input = new FileStream(handle, FileAccess.Read, bufferSize: 1, isAsync: true);
            return new WindowsHidInputCapture(input, preparsedData, decoder, capabilities.InputReportByteLength, device.DevicePath);
        }
        catch
        {
            _ = Native.HidD_FreePreparsedData(preparsedData);
            handle.Dispose();
            throw;
        }
    }

    private sealed class WindowsHidReportDecoder : IRawHidReportDecoder
    {
        private readonly IntPtr preparsedData;
        private readonly IReadOnlyList<ButtonTarget> buttons;
        private readonly IReadOnlyList<HatTarget> hats;
        private readonly IReadOnlyList<ValueTarget> values;
        private readonly IReadOnlyList<IGrouping<(ushort Page, ushort LinkCollection, byte ReportId), ButtonTarget>> buttonGroups;
        private readonly uint maximumUsages;
        private readonly bool reportIdsEnabled;

        public RawDeviceDescriptor Descriptor { get; }
        public int IgnoredValueArrayCount { get; }

        public WindowsHidReportDecoder(IntPtr preparsedData, HidCapabilities capabilities, WindowsHidDevice device, string? retailModelName)
        {
            this.preparsedData = preparsedData;
            if (Marshal.SizeOf<HidpButtonCaps>() != 72 || Marshal.SizeOf<HidpValueCaps>() != 72)
                throw new PlatformNotSupportedException("The Windows HID capability structure layout is unsupported on this runtime.");
            List<ButtonTarget> buttonTargets = ReadButtons(preparsedData, capabilities.NumberInputButtonCaps);
            (List<ValueTarget> valueTargets, List<HatTarget> hatTargets, int ignoredArrays) = ReadValues(preparsedData, capabilities.NumberInputValueCaps);
            if (buttonTargets.Count + valueTargets.Count + (hatTargets.Count * 4) is 0 or > 128)
                throw new InvalidOperationException("Selected HID interface must expose 1..128 independent scalar input controls.");
            this.maximumUsages = 128;
            IgnoredValueArrayCount = ignoredArrays;

            ButtonTarget[] assignedButtons = buttonTargets.OrderBy(target => target.ReportId).ThenBy(target => target.UsagePage).ThenBy(target => target.LinkCollection).ThenBy(target => target.Usage)
                .Select((target, index) => target with { Id = $"button-{index}" }).ToArray();
            var assignedHats = new List<HatTarget>();
            int nextButton = assignedButtons.Length;
            foreach (HatTarget hat in hatTargets.OrderBy(target => target.ReportId).ThenBy(target => target.UsagePage).ThenBy(target => target.LinkCollection).ThenBy(target => target.Usage))
            {
                var ids = new SortedDictionary<ControlId, string>();
                foreach (ControlId direction in new[] { ControlId.DPAD_UP, ControlId.DPAD_DOWN, ControlId.DPAD_LEFT, ControlId.DPAD_RIGHT })
                    ids.Add(direction, $"button-{nextButton++}");
                assignedHats.Add(hat with { ButtonIds = ids });
            }
            buttons = assignedButtons;
            hats = assignedHats.AsReadOnly();
            values = valueTargets.OrderBy(target => target.ReportId).ThenBy(target => target.UsagePage).ThenBy(target => target.LinkCollection).ThenBy(target => target.Usage)
                .Select((target, index) => target with { Id = $"axis-{index}" }).ToArray();
            buttonGroups = buttons.GroupBy(target => (target.UsagePage, target.LinkCollection, target.ReportId)).ToArray();
            reportIdsEnabled = buttons.Any(target => target.ReportId != 0) || hats.Any(target => target.ReportId != 0) || values.Any(target => target.ReportId != 0);

            var controls = buttons.Select(target => new RawControlDescriptor(target.Id, RawControlKind.Button, 0, 1))
                .Concat(hats.SelectMany(target => target.ButtonIds.Values.Select(id => new RawControlDescriptor(id, RawControlKind.Button, 0, 1))))
                .Concat(values.Select(target => new RawControlDescriptor(target.Id, RawControlKind.Axis, target.Minimum, target.Maximum)));
            Descriptor = new RawDeviceDescriptor(
                new DeviceMatchRule(device.VendorId!.Value, device.ProductId!.Value, capabilities.UsagePage, capabilities.Usage),
                controls,
                device.Revision,
                retailModelName,
                device.ConnectionMode);
        }

        public IReadOnlyList<RawInputSample> Decode(ReadOnlyMemory<byte> report, TimeSpan timestamp)
        {
            byte[] buffer = report.ToArray();
            byte reportId = reportIdsEnabled ? buffer[0] : (byte)0;
            var samples = new List<RawInputSample>(Descriptor.Controls.Count);
            foreach (IGrouping<(ushort Page, ushort LinkCollection, byte ReportId), ButtonTarget> group in buttonGroups)
            {
                if (group.Key.ReportId != reportId)
                    continue;
                var usages = new ushort[(int)maximumUsages];
                uint usageCount = (uint)usages.Length;
                int status = Native.HidP_GetUsages(0, group.Key.Page, group.Key.LinkCollection, usages, ref usageCount,
                    preparsedData, buffer, (uint)buffer.Length);
                if (status == HidpStatusIncompatibleReportId)
                    continue;
                if (status != HidpStatusSuccess)
                    throw new InvalidDataException("Windows HID parser could not decode a button input report.");

                var active = usages.AsSpan(0, checked((int)usageCount)).ToArray().ToHashSet();
                foreach (ButtonTarget target in group)
                    samples.Add(new(timestamp, target.Id, active.Contains(target.Usage) ? 1 : 0));
            }

            foreach (HatTarget hat in hats)
            {
                if (hat.ReportId != reportId)
                    continue;
                int status = Native.HidP_GetUsageValue(0, hat.UsagePage, hat.LinkCollection, hat.Usage,
                    out uint rawValue, preparsedData, buffer, (uint)buffer.Length);
                if (status == HidpStatusIncompatibleReportId)
                    continue;
                int? hatValue;
                if (status == HidpStatusNull && hat.HasNull)
                {
                    hatValue = null;
                }
                else if (status == HidpStatusSuccess)
                {
                    double decodedValue = hat.LogicalMinimum < 0 ? SignExtend(rawValue, hat.BitSize) : rawValue;
                    if (decodedValue != Math.Truncate(decodedValue))
                        throw new InvalidDataException("Windows HID parser returned a non-integral D-pad hat position.");
                    hatValue = checked((int)decodedValue);
                }
                else
                {
                    throw new InvalidDataException("Windows HID parser could not decode a D-pad hat input report.");
                }

                foreach ((ControlId direction, bool pressed) in RawHidInputPipeline.DecodeHatSwitch(hatValue, hat.LogicalMinimum, hat.LogicalMaximum))
                    samples.Add(new(timestamp, hat.ButtonIds[direction], pressed ? 1 : 0));
            }

            foreach (ValueTarget target in values)
            {
                if (target.ReportId != reportId)
                    continue;
                int status = Native.HidP_GetUsageValue(0, target.UsagePage, target.LinkCollection, target.Usage,
                    out uint rawValue, preparsedData, buffer, (uint)buffer.Length);
                if (status == HidpStatusIncompatibleReportId)
                    continue;
                if (status == HidpStatusNull)
                    continue;
                if (status != HidpStatusSuccess)
                    throw new InvalidDataException("Windows HID parser could not decode a scalar input report.");

                double value = target.LogicalMinimum < 0 ? SignExtend(rawValue, target.BitSize) : rawValue;
                if (value < target.Minimum || value > target.Maximum)
                    throw new InvalidDataException("Windows HID input value falls outside its declared logical range.");
                samples.Add(new(timestamp, target.Id, value));
            }
            return samples;
        }

        private static List<ButtonTarget> ReadButtons(IntPtr preparsedData, ushort capCount)
        {
            if (capCount == 0)
                return [];
            if (capCount > 128)
                throw new InvalidOperationException("Selected HID interface exposes too many button capability groups.");
            var caps = new HidpButtonCaps[capCount];
            ushort length = capCount;
            int status = Native.HidP_GetButtonCaps(0, caps, ref length, preparsedData);
            if (status != HidpStatusSuccess)
                throw new InvalidOperationException("Windows HID parser could not enumerate input buttons.");

            var result = new List<ButtonTarget>();
            foreach (HidpButtonCaps cap in caps.Take(length))
            {
                ushort minimum = cap.IsRange != 0 ? cap.Data.Range.UsageMinimum : cap.Data.NotRange.Usage;
                ushort maximum = cap.IsRange != 0 ? cap.Data.Range.UsageMaximum : minimum;
                if (maximum < minimum || (uint)result.Count + maximum - minimum + 1 > 128)
                    throw new InvalidOperationException("Selected HID interface exposes too many button usages to map safely.");
                for (uint usage = minimum; usage <= maximum; usage++)
                    result.Add(new(string.Empty, cap.UsagePage, (ushort)usage, cap.LinkCollection, cap.ReportId));
            }
            return result;
        }

        private static (List<ValueTarget> Values, List<HatTarget> Hats, int IgnoredArrays) ReadValues(IntPtr preparsedData, ushort capCount)
        {
            if (capCount == 0)
                return ([], [], 0);
            if (capCount > 128)
                throw new InvalidOperationException("Selected HID interface exposes too many value capability groups.");
            var caps = new HidpValueCaps[capCount];
            ushort length = capCount;
            int status = Native.HidP_GetValueCaps(0, caps, ref length, preparsedData);
            if (status != HidpStatusSuccess)
                throw new InvalidOperationException("Windows HID parser could not enumerate scalar input values.");

            var result = new List<ValueTarget>();
            var hats = new List<HatTarget>();
            int ignoredArrays = 0;
            foreach (HidpValueCaps cap in caps.Take(length))
            {
                if (cap.ReportCount != 1)
                {
                    ignoredArrays++;
                    continue;
                }
                ushort usageMinimum = cap.IsRange != 0 ? cap.Data.Range.UsageMinimum : cap.Data.NotRange.Usage;
                ushort usageMaximum = cap.IsRange != 0 ? cap.Data.Range.UsageMaximum : usageMinimum;
                if (usageMaximum < usageMinimum || cap.BitSize is 0 or > 32 || cap.LogicalMinimum >= cap.LogicalMaximum)
                    throw new InvalidOperationException("Selected HID interface exposes an unsupported or excessive input value range.");
                for (uint usage = usageMinimum; usage <= usageMaximum; usage++)
                {
                    if (cap.UsagePage == 1 && usage == 0x39)
                    {
                        if (cap.HasNull != 0 && (long)cap.LogicalMaximum - cap.LogicalMinimum == 7)
                            hats.Add(new(cap.UsagePage, (ushort)usage, cap.LinkCollection, cap.ReportId, cap.BitSize,
                                cap.LogicalMinimum, cap.LogicalMaximum, HasNull: true));
                        else
                            ignoredArrays++;
                        continue;
                    }
                    result.Add(new(string.Empty, cap.UsagePage, (ushort)usage, cap.LinkCollection, cap.ReportId,
                        cap.BitSize, cap.LogicalMinimum, cap.LogicalMaximum));
                    if (result.Count + (hats.Count * 4) > 128)
                        throw new InvalidOperationException("Selected HID interface exposes more than 128 independent input controls.");
                }
            }
            return (result, hats, ignoredArrays);
        }

        private static int SignExtend(uint value, ushort bitSize)
        {
            if (bitSize == 32)
                return unchecked((int)value);
            uint mask = (1u << bitSize) - 1;
            uint masked = value & mask;
            return (masked & (1u << (bitSize - 1))) == 0
                ? checked((int)masked)
                : unchecked((int)(masked | ~mask));
        }

        private sealed record ButtonTarget(string Id, ushort UsagePage, ushort Usage, ushort LinkCollection, byte ReportId);
        private sealed record HatTarget(ushort UsagePage, ushort Usage, ushort LinkCollection, byte ReportId, ushort BitSize,
            int LogicalMinimum, int LogicalMaximum, bool HasNull, IReadOnlyDictionary<ControlId, string> ButtonIds = null!);
        private sealed record ValueTarget(string Id, ushort UsagePage, ushort Usage, ushort LinkCollection, byte ReportId, ushort BitSize, int LogicalMinimum, int LogicalMaximum)
        {
            public double Minimum => LogicalMinimum;
            public double Maximum => LogicalMaximum;
        }
    }

    public sealed class WindowsHidInputCapture : IDisposable
    {
        private readonly FileStream input;
        private readonly IntPtr preparsedData;
        private readonly IRawHidReportDecoder decoder;
        private readonly ushort reportBytes;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private bool disposed;

        internal WindowsHidInputCapture(FileStream input, IntPtr preparsedData, IRawHidReportDecoder decoder, ushort reportBytes, string selectedDevicePath)
        {
            this.input = input;
            this.preparsedData = preparsedData;
            this.decoder = decoder;
            this.reportBytes = reportBytes;
            LocalUnitFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selectedDevicePath))).ToLowerInvariant();
        }

        public RawDeviceDescriptor Descriptor => decoder.Descriptor;
        public int IgnoredValueArrayCount => ((WindowsHidReportDecoder)decoder).IgnoredValueArrayCount;
        public TimeSpan Timestamp => clock.Elapsed;

        /// <summary>A local-only hash used to pair calibration with the selected HID interface.</summary>
        public string LocalUnitFingerprint { get; }

        public async IAsyncEnumerable<IReadOnlyList<RawInputSample>> ReadReportsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(WindowsHidInputCapture));
            byte[] buffer = new byte[reportBytes];
            while (true)
            {
                int count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    throw new EndOfStreamException("The selected HID interface disconnected.");
                ReadOnlyMemory<byte> report = buffer.AsMemory(0, count).ToArray();
                yield return RawHidInputPipeline.DecodeReport(decoder, report, clock.Elapsed);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            input.Dispose();
            _ = Native.HidD_FreePreparsedData(preparsedData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpUsageRange
    {
        public ushort UsageMinimum;
        public ushort UsageMaximum;
        public ushort StringMinimum;
        public ushort StringMaximum;
        public ushort DesignatorMinimum;
        public ushort DesignatorMaximum;
        public ushort DataIndexMinimum;
        public ushort DataIndexMaximum;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpUsageNotRange
    {
        public ushort Usage;
        public ushort Reserved1;
        public ushort StringIndex;
        public ushort Reserved2;
        public ushort DesignatorIndex;
        public ushort Reserved3;
        public ushort DataIndex;
        public ushort Reserved4;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct HidpButtonCapUnion
    {
        [FieldOffset(0)] public HidpUsageRange Range;
        [FieldOffset(0)] public HidpUsageNotRange NotRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpButtonCaps
    {
        public ushort UsagePage;
        public byte ReportId;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public ushort ReportCount;
        public ushort Reserved2;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved3;
        public uint Reserved4;
        public uint Reserved5;
        public uint Reserved6;
        public uint Reserved7;
        public uint Reserved8;
        public uint Reserved9;
        public HidpButtonCapUnion Data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct HidpValueCapUnion
    {
        [FieldOffset(0)] public HidpUsageRange Range;
        [FieldOffset(0)] public HidpUsageNotRange NotRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpValueCaps
    {
        public ushort UsagePage;
        public byte ReportId;
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
        public ushort Reserved2_0;
        public ushort Reserved2_1;
        public ushort Reserved2_2;
        public ushort Reserved2_3;
        public ushort Reserved2_4;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMinimum;
        public int LogicalMaximum;
        public int PhysicalMinimum;
        public int PhysicalMaximum;
        public HidpValueCapUnion Data;
    }

    private static partial class Native
    {
        [DllImport("hid.dll", ExactSpelling = true)]
        internal static extern int HidP_GetButtonCaps(int reportType, [Out] HidpButtonCaps[] buttonCaps, ref ushort buttonCapsLength, IntPtr preparsedData);

        [DllImport("hid.dll", ExactSpelling = true)]
        internal static extern int HidP_GetValueCaps(int reportType, [Out] HidpValueCaps[] valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

        [DllImport("hid.dll", ExactSpelling = true)]
        internal static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usageList, ref uint usageLength, IntPtr preparsedData, byte[] report, uint reportLength);

        [DllImport("hid.dll", ExactSpelling = true)]
        internal static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint usageValue, IntPtr preparsedData, byte[] report, uint reportLength);
    }
}
