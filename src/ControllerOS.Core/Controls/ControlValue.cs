namespace ControllerOS.Core.Controls;

public readonly record struct ControlValue
{
    public ControlId Id { get; }
    public double Value { get; }
    public ControlKind Kind => ControlCatalog.KindOf(Id);

    private ControlValue(ControlId id, double value)
    {
        if (!Enum.IsDefined(id))
            throw new ArgumentOutOfRangeException(nameof(id));
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Control values must be finite.");

        double minimum = KindMinimum(id);
        double maximum = KindMaximum(id);
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), $"{id} must be in {minimum}..{maximum}.");

        Id = id;
        Value = value;
    }

    public bool IsPressed => Kind == ControlKind.Button && Value == 1.0;

    public static ControlValue Button(ControlId id, bool pressed)
    {
        RequireKind(id, ControlKind.Button);
        return new ControlValue(id, pressed ? 1.0 : 0.0);
    }

    public static ControlValue Axis(ControlId id, double value)
    {
        RequireKind(id, ControlKind.Axis);
        return new ControlValue(id, value);
    }

    public static ControlValue Trigger(ControlId id, double value)
    {
        RequireKind(id, ControlKind.Trigger);
        return new ControlValue(id, value);
    }

    public static ControlValue FromNormalized(ControlId id, double value) => ControlCatalog.KindOf(id) switch
    {
        ControlKind.Button when value is 0.0 or 1.0 => new ControlValue(id, value),
        ControlKind.Button => throw new ArgumentOutOfRangeException(nameof(value), "Button values must be 0 or 1."),
        _ => new ControlValue(id, value)
    };

    private static double KindMinimum(ControlId id) => ControlCatalog.KindOf(id) switch
    {
        ControlKind.Axis => -1.0,
        _ => 0.0
    };

    private static double KindMaximum(ControlId id) => ControlCatalog.KindOf(id) switch
    {
        ControlKind.Button => 1.0,
        _ => 1.0
    };

    private static void RequireKind(ControlId id, ControlKind expected)
    {
        if (!Enum.IsDefined(id) || ControlCatalog.KindOf(id) != expected)
            throw new ArgumentException($"{id} is not a {expected} control.", nameof(id));
    }
}
