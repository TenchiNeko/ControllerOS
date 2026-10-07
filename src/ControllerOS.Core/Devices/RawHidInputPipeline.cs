using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Devices;

/// <summary>Decodes one already-selected HID interface's input report.</summary>
public interface IRawHidReportDecoder
{
    RawDeviceDescriptor Descriptor { get; }
    IReadOnlyList<RawInputSample> Decode(ReadOnlyMemory<byte> report, TimeSpan timestamp);
}

/// <summary>Applies common bounds and validates observations before teaching.</summary>
public static class RawHidInputPipeline
{
    public const int MaximumReportBytes = 4096;

    public static IReadOnlyDictionary<ControlId, bool> DecodeHatSwitch(int? value, int logicalMinimum, int logicalMaximum)
    {
        if ((long)logicalMaximum - logicalMinimum != 7)
            throw new ArgumentException("A D-pad hat must expose exactly eight logical directions.");
        var result = new SortedDictionary<ControlId, bool>
        {
            [ControlId.DPAD_UP] = false,
            [ControlId.DPAD_DOWN] = false,
            [ControlId.DPAD_LEFT] = false,
            [ControlId.DPAD_RIGHT] = false
        };
        if (value is null)
            return result;
        int position = value.Value - logicalMinimum;
        if (position is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(value), "D-pad hat value is outside the eight logical directions.");

        result[ControlId.DPAD_UP] = position is 0 or 1 or 7;
        result[ControlId.DPAD_RIGHT] = position is 1 or 2 or 3;
        result[ControlId.DPAD_DOWN] = position is 3 or 4 or 5;
        result[ControlId.DPAD_LEFT] = position is 5 or 6 or 7;
        return result;
    }

    public static IReadOnlyList<RawInputSample> DecodeReport(
        IRawHidReportDecoder decoder,
        ReadOnlyMemory<byte> report,
        TimeSpan timestamp)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        if (report.Length is 0 or > MaximumReportBytes)
            throw new ArgumentOutOfRangeException(nameof(report), $"HID input reports must contain 1..{MaximumReportBytes} bytes.");
        if (timestamp < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timestamp));

        IReadOnlyList<RawInputSample> decoded = decoder.Decode(report, timestamp)
            ?? throw new InvalidOperationException("HID decoder returned no observation list.");
        if (decoded.Count > decoder.Descriptor.Controls.Count)
            throw new InvalidOperationException("HID decoder returned more observations than declared controls.");
        var controls = decoder.Descriptor.Controls.ToDictionary(control => control.Id, StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var validated = new RawInputSample[decoded.Count];
        for (int index = 0; index < decoded.Count; index++)
        {
            RawInputSample sample = decoded[index] ?? throw new InvalidOperationException("HID decoder returned a null observation.");
            if (sample.Timestamp != timestamp || !double.IsFinite(sample.Value) ||
                !controls.TryGetValue(sample.ControlId, out RawControlDescriptor? control) || !ids.Add(sample.ControlId) ||
                sample.Value < control.Minimum || sample.Value > control.Maximum)
                throw new InvalidOperationException("HID decoder returned an invalid or duplicate control observation.");
            validated[index] = sample;
        }
        return Array.AsReadOnly(validated);
    }
}
