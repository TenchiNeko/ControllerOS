using ControllerOS.Core.Devices;

namespace ControllerOS.Core.Simulation;

public static class SyntheticUnknownDeviceFixture
{
    public static RawDeviceDescriptor Device { get; } = new(
        new DeviceMatchRule(0x1234, 0x5678, 0x0001, 0x0005),
        [
            new("button-0", RawControlKind.Button, 0, 1),
            new("axis-0", RawControlKind.Axis, -1000, 1000),
            new("axis-1", RawControlKind.Axis, -1000, 1000),
            new("axis-2", RawControlKind.Axis, 0, 255)
        ]);

    public static IReadOnlyList<RawInputSample> SouthButtonSamples() =>
    [
        new(TimeSpan.Zero, "button-0", 0),
        new(TimeSpan.FromMilliseconds(10), "button-0", 1),
        new(TimeSpan.FromMilliseconds(20), "button-0", 0)
    ];

    public static IReadOnlyList<RawInputSample> LeftStickXSamples() =>
    [
        new(TimeSpan.Zero, "axis-0", 10),
        new(TimeSpan.FromMilliseconds(5), "axis-0", 12),
        new(TimeSpan.FromMilliseconds(10), "axis-0", 8),
        new(TimeSpan.FromMilliseconds(15), "axis-0", -1000),
        new(TimeSpan.FromMilliseconds(20), "axis-0", 10),
        new(TimeSpan.FromMilliseconds(25), "axis-0", 1000),
        new(TimeSpan.FromMilliseconds(30), "axis-0", 11)
    ];

    public static IReadOnlyList<RawInputSample> LeftStickYSamples() =>
    [
        new(TimeSpan.Zero, "axis-1", -2),
        new(TimeSpan.FromMilliseconds(5), "axis-1", 0),
        new(TimeSpan.FromMilliseconds(10), "axis-1", 2),
        new(TimeSpan.FromMilliseconds(15), "axis-1", -1000),
        new(TimeSpan.FromMilliseconds(20), "axis-1", 0),
        new(TimeSpan.FromMilliseconds(25), "axis-1", 1000),
        new(TimeSpan.FromMilliseconds(30), "axis-1", 1)
    ];

    public static IReadOnlyList<RawInputSample> TriggerSamples() =>
    [
        new(TimeSpan.Zero, "axis-2", 0),
        new(TimeSpan.FromMilliseconds(5), "axis-2", 1),
        new(TimeSpan.FromMilliseconds(10), "axis-2", 0),
        new(TimeSpan.FromMilliseconds(15), "axis-2", 255),
        new(TimeSpan.FromMilliseconds(20), "axis-2", 100),
        new(TimeSpan.FromMilliseconds(25), "axis-2", 0)
    ];
}
