using System.Collections.ObjectModel;

namespace ControllerOS.Core.Controls;

public sealed class OutputState
{
    private readonly ReadOnlyDictionary<ControlId, ControlValue> values;

    public static OutputState Neutral { get; } = new(ControlCatalog.All.Select(id => ControlValue.FromNormalized(id, 0.0)));

    public OutputState(IEnumerable<ControlValue>? values = null)
    {
        var map = new SortedDictionary<ControlId, ControlValue>();
        foreach (ControlId id in ControlCatalog.All)
            map.Add(id, ControlValue.FromNormalized(id, 0.0));
        if (values is not null)
        {
            foreach (ControlValue value in values)
                map[value.Id] = value;
        }
        this.values = new ReadOnlyDictionary<ControlId, ControlValue>(map);
    }

    public IReadOnlyDictionary<ControlId, ControlValue> Values => values;
    public ControlValue Get(ControlId id) => values[id];

    public OutputState WithValue(ControlValue value) =>
        new(values.Values.Where(existing => existing.Id != value.Id).Append(value));

    public OutputState WithNeutralValue(ControlId id) => WithValue(ControlValue.FromNormalized(id, 0.0));
}
