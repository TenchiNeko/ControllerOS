namespace ControllerOS.Core.Controls;

public enum ControlId
{
    SOUTH,
    EAST,
    WEST,
    NORTH,
    LEFT_BUMPER,
    RIGHT_BUMPER,
    LEFT_TRIGGER,
    RIGHT_TRIGGER,
    LEFT_STICK_X,
    LEFT_STICK_Y,
    RIGHT_STICK_X,
    RIGHT_STICK_Y,
    LEFT_STICK_CLICK,
    RIGHT_STICK_CLICK,
    DPAD_UP,
    DPAD_DOWN,
    DPAD_LEFT,
    DPAD_RIGHT,
    MENU,
    VIEW,
    GUIDE
}

public enum ControlKind
{
    Button,
    Axis,
    Trigger
}

public static class ControlCatalog
{
    public static IReadOnlyList<ControlId> All { get; } = Array.AsReadOnly(Enum.GetValues<ControlId>());
    private static readonly IReadOnlyDictionary<string, ControlId> ScriptAliases = new Dictionary<string, ControlId>(StringComparer.OrdinalIgnoreCase)
    {
        ["LEFT_X"] = ControlId.LEFT_STICK_X,
        ["LEFT_Y"] = ControlId.LEFT_STICK_Y,
        ["RIGHT_X"] = ControlId.RIGHT_STICK_X,
        ["RIGHT_Y"] = ControlId.RIGHT_STICK_Y
    };

    public static ControlKind KindOf(ControlId id)
    {
        if (!Enum.IsDefined(id))
            throw new ArgumentOutOfRangeException(nameof(id));

        return id switch
        {
            ControlId.LEFT_TRIGGER or ControlId.RIGHT_TRIGGER => ControlKind.Trigger,
            ControlId.LEFT_STICK_X or ControlId.LEFT_STICK_Y or ControlId.RIGHT_STICK_X or ControlId.RIGHT_STICK_Y => ControlKind.Axis,
            _ => ControlKind.Button
        };
    }

    public static bool TryParse(string name, out ControlId id)
    {
        id = default;
        if (string.IsNullOrEmpty(name) || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            return false;
        if (ScriptAliases.TryGetValue(name, out id))
            return true;
        return Enum.TryParse(name, ignoreCase: true, out id) && Enum.IsDefined(id);
    }
}
