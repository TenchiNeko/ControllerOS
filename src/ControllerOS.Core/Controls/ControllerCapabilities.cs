using System.Collections.Frozen;

namespace ControllerOS.Core.Controls;

public sealed class ControllerCapabilities
{
    private readonly FrozenSet<ControlId> controls;

    public ControllerCapabilities(IEnumerable<ControlId> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ControlId[] requested = controls.Take(ControlCatalog.All.Count + 1).ToArray();
        if (requested.Length > ControlCatalog.All.Count)
            throw new ArgumentException($"Capabilities cannot contain more than {ControlCatalog.All.Count} controls.", nameof(controls));
        this.controls = requested.ToFrozenSet();
        if (this.controls.Any(id => !Enum.IsDefined(id)))
            throw new ArgumentException("Capabilities contain an unknown control.", nameof(controls));
    }

    public IReadOnlySet<ControlId> Controls => controls;
    public bool Supports(ControlId id) => controls.Contains(id);

    public static ControllerCapabilities Standard { get; } = new(ControlCatalog.All);
}
