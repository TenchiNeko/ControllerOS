using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Input;

public enum InputEventKind
{
    Press,
    Release,
    Change
}

public sealed record ControllerInputEvent(
    TimeSpan Timestamp,
    long Sequence,
    ControlId Control,
    ControlValue Previous,
    ControlValue Current,
    InputEventKind Kind)
{
    public static ControllerInputEvent? Create(TimeSpan timestamp, long sequence, ControlValue previous, ControlValue current)
    {
        if (previous.Id != current.Id)
            throw new ArgumentException("A transition must refer to one control.");
        if (timestamp < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (previous == current)
            return null;

        InputEventKind kind = previous.Kind == ControlKind.Button
            ? current.IsPressed ? InputEventKind.Press : InputEventKind.Release
            : InputEventKind.Change;
        return new ControllerInputEvent(timestamp, sequence, current.Id, previous, current, kind);
    }
}
