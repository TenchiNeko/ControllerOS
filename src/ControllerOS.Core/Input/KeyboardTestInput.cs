using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Input;

public sealed class KeyboardTestInput
{
    private static readonly IReadOnlyDictionary<string, ControlId> Buttons = new Dictionary<string, ControlId>(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = ControlId.SOUTH,
        ["E"] = ControlId.EAST,
        ["Q"] = ControlId.WEST,
        ["R"] = ControlId.NORTH,
        ["U"] = ControlId.LEFT_BUMPER,
        ["I"] = ControlId.RIGHT_BUMPER,
        ["LeftCtrl"] = ControlId.LEFT_STICK_CLICK,
        ["RightCtrl"] = ControlId.RIGHT_STICK_CLICK,
        ["NumPad8"] = ControlId.DPAD_UP,
        ["NumPad2"] = ControlId.DPAD_DOWN,
        ["NumPad4"] = ControlId.DPAD_LEFT,
        ["NumPad6"] = ControlId.DPAD_RIGHT,
        ["Enter"] = ControlId.MENU,
        ["Tab"] = ControlId.VIEW,
        ["F1"] = ControlId.GUIDE
    };

    private static readonly HashSet<string> SupportedKeys = new(Buttons.Keys.Concat(
    [
        "W", "A", "S", "D", "Up", "Down", "Left", "Right", "LeftShift", "RightShift"
    ]), StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ControlId, ControlValue> values = ControlCatalog.All.ToDictionary(id => id, id => ControlValue.FromNormalized(id, 0.0));
    private TimeSpan lastTimestamp;
    private long nextSequence;

    public ControllerCapabilities Capabilities { get; } = ControllerCapabilities.Standard;

    public static bool SupportsKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && SupportedKeys.Contains(NormalizeKey(key));

    public IReadOnlyList<ControllerInputEvent> SetKey(string key, bool isDown, TimeSpan timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (timestamp < lastTimestamp)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Keyboard input timestamps must be nondecreasing.");

        string normalizedKey = NormalizeKey(key);
        if (!SupportsKey(normalizedKey))
            return Array.Empty<ControllerInputEvent>();

        bool changed = isDown ? pressedKeys.Add(normalizedKey) : pressedKeys.Remove(normalizedKey);
        if (!changed)
            return Array.Empty<ControllerInputEvent>();

        lastTimestamp = timestamp;
        return Recompute(timestamp);
    }

    public IReadOnlyList<ControllerInputEvent> ReleaseAll(TimeSpan timestamp)
    {
        if (timestamp < lastTimestamp)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Keyboard input timestamps must be nondecreasing.");

        pressedKeys.Clear();
        lastTimestamp = timestamp;
        return Recompute(timestamp);
    }

    public ControllerState Snapshot(TimeSpan timestamp)
    {
        if (timestamp < lastTimestamp)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Snapshot timestamp cannot precede keyboard input.");

        ControllerState state = ControllerState.Neutral(timestamp, Capabilities);
        foreach (ControlValue value in values.Values)
            state = state.WithValue(timestamp, value);
        return state;
    }

    private static string NormalizeKey(string key)
    {
        string normalized = key.Trim();
        return normalized.ToUpperInvariant() switch
        {
            "LEFTCTRL" => "LeftCtrl",
            "RIGHTCTRL" => "RightCtrl",
            "NUMPAD8" => "NumPad8",
            "NUMPAD2" => "NumPad2",
            "NUMPAD4" => "NumPad4",
            "NUMPAD6" => "NumPad6",
            _ => normalized
        };
    }

    private IReadOnlyList<ControllerInputEvent> Recompute(TimeSpan timestamp)
    {
        var events = new List<ControllerInputEvent>();
        foreach (ControlId control in ControlCatalog.All)
        {
            double value = ControlCatalog.KindOf(control) switch
            {
                ControlKind.Button => ButtonValue(control),
                ControlKind.Axis => AxisValue(control),
                ControlKind.Trigger => IsTriggerDown(control) ? 1.0 : 0.0,
                _ => throw new ArgumentOutOfRangeException(nameof(control))
            };
            AddTransition(control, value, timestamp, events);
        }

        return events.AsReadOnly();
    }

    private double ButtonValue(ControlId control) =>
        Buttons.FirstOrDefault(pair => pair.Value == control) is { Key: not null } pair && pressedKeys.Contains(pair.Key) ? 1.0 : 0.0;

    private double AxisValue(ControlId control) => control switch
    {
        ControlId.LEFT_STICK_X => DirectionValue("D", "A"),
        ControlId.LEFT_STICK_Y => DirectionValue("W", "S"),
        ControlId.RIGHT_STICK_X => DirectionValue("Right", "Left"),
        ControlId.RIGHT_STICK_Y => DirectionValue("Up", "Down"),
        _ => throw new ArgumentOutOfRangeException(nameof(control))
    };

    private double DirectionValue(string positiveKey, string negativeKey) =>
        pressedKeys.Contains(positiveKey) == pressedKeys.Contains(negativeKey)
            ? 0.0
            : pressedKeys.Contains(positiveKey) ? 1.0 : -1.0;

    private bool IsTriggerDown(ControlId trigger) => trigger switch
    {
        ControlId.LEFT_TRIGGER => pressedKeys.Contains("LeftShift"),
        ControlId.RIGHT_TRIGGER => pressedKeys.Contains("RightShift"),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger))
    };

    private void AddTransition(ControlId id, double value, TimeSpan timestamp, List<ControllerInputEvent> events)
    {
        ControlValue next = ControlValue.FromNormalized(id, value);
        ControlValue previous = values[id];
        ControllerInputEvent? inputEvent = ControllerInputEvent.Create(timestamp, nextSequence, previous, next);
        if (inputEvent is null)
            return;

        values[id] = next;
        nextSequence++;
        events.Add(inputEvent);
    }
}
