namespace ControllerOS.Windows;

/// <summary>
/// Describes one present HID interface. <see cref="DevicePath"/> is a local
/// Windows handle path: use it to open this interface, but never include it in
/// a community report.
/// </summary>
public sealed record WindowsHidDevice(
    string DevicePath,
    string? Manufacturer,
    string? Product,
    ushort? VendorId,
    ushort? ProductId,
    ushort? Revision,
    ushort? InterfaceNumber,
    ushort? UsagePage,
    ushort? Usage,
    ushort? InputReportBytes,
    ushort? OutputReportBytes,
    ushort? InputButtonCapabilityCount,
    ushort? InputValueCapabilityCount);

/// <summary>Windows HID enumeration boundary for the device catalog and UI.</summary>
public interface IWindowsDeviceEnumerator
{
    IReadOnlyList<WindowsHidDevice> Enumerate();
}
