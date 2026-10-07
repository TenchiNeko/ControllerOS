using ControllerOS.Core.Controls;
using ControllerOS.Core.Input;

namespace ControllerOS.Core.Simulation;

public static class SyntheticDeviceFixtures
{
    public static ControllerCapabilities Standard { get; } = ControllerCapabilities.Standard;

    public static ControllerCapabilities MissingTriggers { get; } = new(
        ControlCatalog.All.Where(id => id is not ControlId.LEFT_TRIGGER and not ControlId.RIGHT_TRIGGER));

    public static SyntheticInput OffCenterStick() => new(Standard,
    [
        new(TimeSpan.FromMilliseconds(0), ControlValue.Axis(ControlId.LEFT_STICK_X, 0.08)),
        new(TimeSpan.FromMilliseconds(100), ControlValue.Axis(ControlId.LEFT_STICK_X, 0.12)),
        new(TimeSpan.FromMilliseconds(200), ControlValue.Axis(ControlId.LEFT_STICK_X, 0.09))
    ]);

    public static SyntheticInput NoisyStick() => new(Standard,
    [
        new(TimeSpan.FromMilliseconds(0), ControlValue.Axis(ControlId.LEFT_STICK_X, -0.02)),
        new(TimeSpan.FromMilliseconds(4), ControlValue.Axis(ControlId.LEFT_STICK_X, 0.01)),
        new(TimeSpan.FromMilliseconds(8), ControlValue.Axis(ControlId.LEFT_STICK_X, -0.01)),
        new(TimeSpan.FromMilliseconds(12), ControlValue.Axis(ControlId.LEFT_STICK_X, 0.02))
    ]);

    public static SyntheticInput StandardButtonPress() => new(Standard,
    [
        new(TimeSpan.FromMilliseconds(10), ControlValue.Button(ControlId.SOUTH, true)),
        new(TimeSpan.FromMilliseconds(30), ControlValue.Button(ControlId.SOUTH, false))
    ]);
}
