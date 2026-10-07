using System.Collections.ObjectModel;

namespace ControllerOS.Core.Controls;

public sealed class ControllerState
{
    private readonly ReadOnlyDictionary<ControlId, ControlValue> values;

    public TimeSpan Timestamp { get; }
    public ControllerCapabilities Capabilities { get; }

    public ControllerState(TimeSpan timestamp, ControllerCapabilities capabilities, IEnumerable<ControlValue>? initialValues = null)
    {
        if (timestamp < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timestamp));

        ArgumentNullException.ThrowIfNull(capabilities);
        Timestamp = timestamp;
        Capabilities = capabilities;

        var map = new SortedDictionary<ControlId, ControlValue>();
        foreach (ControlId id in capabilities.Controls.Order())
            map.Add(id, ControlValue.FromNormalized(id, 0.0));

        if (initialValues is not null)
        {
            foreach (ControlValue value in initialValues)
            {
                if (!capabilities.Supports(value.Id))
                    throw new ArgumentException($"Initial value provided for unsupported control {value.Id}.", nameof(initialValues));
                map[value.Id] = value;
            }
        }

        values = new ReadOnlyDictionary<ControlId, ControlValue>(map);
    }

    public IReadOnlyDictionary<ControlId, ControlValue> Values => values;

    public ControlValue Get(ControlId id) => values.TryGetValue(id, out ControlValue value)
        ? value
        : throw new InvalidOperationException($"Controller does not support {id}.");

    public ControllerState WithValue(TimeSpan timestamp, ControlValue value)
    {
        if (timestamp < Timestamp)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Controller timestamps cannot move backwards.");
        if (!Capabilities.Supports(value.Id))
            throw new InvalidOperationException($"Controller does not support {value.Id}.");

        return new ControllerState(timestamp, Capabilities, values.Values.Where(existing => existing.Id != value.Id).Append(value));
    }

    public static ControllerState Neutral(TimeSpan timestamp, ControllerCapabilities? capabilities = null) =>
        new(timestamp, capabilities ?? ControllerCapabilities.Standard);
}
