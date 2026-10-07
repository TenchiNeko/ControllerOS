using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Devices;

public sealed record RawInputSample(TimeSpan Timestamp, string ControlId, double Value);

public sealed class DeviceTeachingSession
{
    private readonly RawDeviceDescriptor device;
    private readonly Dictionary<ControlId, DeviceControlMapping> mappings = [];
    private readonly Dictionary<string, ControlCalibration> calibrations = new(StringComparer.Ordinal);
    private readonly HashSet<ControlId> skipped = [];
    private readonly string calibrationId = Guid.NewGuid().ToString("N");

    public DeviceTeachingSession(RawDeviceDescriptor device)
    {
        ArgumentNullException.ThrowIfNull(device);
        this.device = device;
    }

    public RawDeviceDescriptor Device => device;
    public IReadOnlyCollection<ControlId> SkippedControls => Array.AsReadOnly(skipped.Order().ToArray());
    public IReadOnlyCollection<ControlId> IdentifiedControls => Array.AsReadOnly(mappings.Keys.Order().ToArray());
    public bool IsComplete => mappings.Count + skipped.Count == ControlCatalog.All.Count;

    public DeviceControlMapping? GetIdentifiedMapping(ControlId target) => mappings.GetValueOrDefault(target);

    public void Observe(ControlId target, IEnumerable<RawInputSample> samples)
    {
        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target));
        if (mappings.ContainsKey(target) || skipped.Contains(target))
            throw new InvalidOperationException($"{target} has already been identified or skipped.");
        ArgumentNullException.ThrowIfNull(samples);

        RawInputSample[] observations = samples.Take(ControlCalibration.MaximumSampleCount + 1).ToArray();
        if (observations.Length is < 2 or > ControlCalibration.MaximumSampleCount)
            throw new ArgumentException($"Provide between 2 and {ControlCalibration.MaximumSampleCount} timestamped samples.", nameof(samples));
        ValidateSamples(observations);

        ControlKind targetKind = ControlCatalog.KindOf(target);
        RawControlDescriptor[] candidates = device.Controls.Where(control =>
        {
            if (targetKind == ControlKind.Button && control.Kind != RawControlKind.Button)
                return false;
            if (targetKind != ControlKind.Button && control.Kind != RawControlKind.Axis)
                return false;

            RawInputSample[] values = observations.Where(sample => sample.ControlId == control.Id).ToArray();
            if (values.Length < 2)
                return false;
            double minimum = values.Min(sample => sample.Value);
            double maximum = values.Max(sample => sample.Value);
            double range = control.Maximum - control.Minimum;
            if (targetKind == ControlKind.Button)
                return minimum <= control.Minimum + range * 0.1 && maximum >= control.Maximum - range * 0.1;
            return maximum - minimum >= range * 0.05;
        }).ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException($"No changing raw control was observed for {target}.");
        if (candidates.Length > 1)
            throw new InvalidOperationException($"More than one raw control changed while identifying {target}; move one control at a time.");

        RawControlDescriptor raw = candidates[0];
        if (mappings.Values.Any(mapping => mapping.RawControlId == raw.Id))
            throw new InvalidOperationException($"Raw control '{raw.Id}' is already assigned to another standard control.");
        RawInputSample[] rawSamples = observations.Where(sample => sample.ControlId == raw.Id).ToArray();
        double observedMinimum = rawSamples.Min(sample => sample.Value);
        double observedMaximum = rawSamples.Max(sample => sample.Value);
        double center;
        double noise;
        if (targetKind == ControlKind.Button)
        {
            center = observedMinimum;
            noise = 0;
        }
        else
        {
            double range = raw.Maximum - raw.Minimum;
            var restSamples = new List<double>();
            double initial = rawSamples[0].Value;
            foreach (RawInputSample sample in rawSamples)
            {
                if (Math.Abs(sample.Value - initial) > range * 0.1)
                    break;
                restSamples.Add(sample.Value);
            }
            if (restSamples.Count < 3)
                throw new InvalidOperationException($"At least three initial resting samples are required for {target} center/noise calibration.");

            center = Median(restSamples);
            noise = restSamples.Max(value => Math.Abs(value - center));
            if (targetKind == ControlKind.Axis &&
                (center <= observedMinimum || center >= observedMaximum || noise >= Math.Min(center - observedMinimum, observedMaximum - center)))
                throw new InvalidOperationException($"Samples for {target} must include movement on both sides of its resting center beyond the measured noise.");
        }

        MappingMode mode = targetKind switch
        {
            ControlKind.Button => MappingMode.Button,
            ControlKind.Axis => MappingMode.Axis,
            ControlKind.Trigger => MappingMode.Trigger,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        mappings.Add(target, new(raw.Id, target, raw.Kind, mode, raw.Minimum, raw.Maximum));
        calibrations.Add(raw.Id, new(observedMinimum, observedMaximum, center, noise, rawSamples.Length, mode));
    }

    public void Skip(ControlId target)
    {
        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target));
        if (mappings.ContainsKey(target) || !skipped.Add(target))
            throw new InvalidOperationException($"{target} has already been identified or skipped.");
    }

    public DeviceDefinition Preview()
    {
        if (!IsComplete)
            throw new InvalidOperationException("Identify or skip each standard control before previewing the complete mapping.");
        if (mappings.Count == 0)
            throw new InvalidOperationException("At least one standard control must be identified.");

        DeviceMatchRule match = device.Match;
        string id = $"hid-{match.VendorId:x4}-{match.ProductId:x4}-{match.UsagePage:x4}-{match.Usage:x4}";
        var definition = new DeviceDefinition(
            DeviceDefinition.CurrentSchemaVersion,
            id,
            $"Unknown controller {match.VendorId:x4}:{match.ProductId:x4}",
            match,
            Array.AsReadOnly(mappings.Values.OrderBy(mapping => mapping.Target).ToArray()));
        DeviceDefinitionValidationResult validation = DeviceDefinitionValidator.Validate(definition);
        if (!validation.IsValid)
            throw new DeviceDefinitionFormatException(string.Join(Environment.NewLine, validation.Errors));
        return definition;
    }

    public DeviceCalibration PreviewCalibration()
    {
        if (!IsComplete || mappings.Count == 0)
            throw new InvalidOperationException("Complete the control teaching flow before previewing calibration.");
        string definitionId = $"hid-{device.Match.VendorId:x4}-{device.Match.ProductId:x4}-{device.Match.UsagePage:x4}-{device.Match.Usage:x4}";
        var calibration = new DeviceCalibration(DeviceCalibration.CurrentSchemaVersion, calibrationId, definitionId,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, ControlCalibration>(new SortedDictionary<string, ControlCalibration>(calibrations, StringComparer.Ordinal)));
        if (!calibration.IsValid)
            throw new DeviceDefinitionFormatException("Teaching samples did not produce valid per-unit calibration values.");
        return calibration;
    }

    public DeviceDefinitionValidationResult Validate()
    {
        if (!IsComplete)
            return new(false, Array.AsReadOnly(new[] { "Identify or skip each standard control before saving." }));
        if (mappings.Count == 0)
            return new(false, Array.AsReadOnly(new[] { "At least one standard control must be identified." }));
        DeviceDefinitionValidationResult definitionValidation = DeviceDefinitionValidator.Validate(Preview());
        if (!definitionValidation.IsValid)
            return definitionValidation;
        try
        {
            _ = PreviewCalibration();
            return new(true, Array.Empty<string>());
        }
        catch (DeviceDefinitionFormatException exception)
        {
            return new(false, Array.AsReadOnly(new[] { exception.Message }));
        }
    }

    public SavedDeviceDefinition Save(DeviceDefinitionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        DeviceDefinitionValidationResult validation = Validate();
        if (!validation.IsValid)
            throw new DeviceDefinitionFormatException(string.Join(Environment.NewLine, validation.Errors));
        return store.Save(Preview(), PreviewCalibration());
    }

    private void ValidateSamples(IReadOnlyList<RawInputSample> samples)
    {
        var controls = device.Controls.ToDictionary(control => control.Id, StringComparer.Ordinal);
        TimeSpan previousTimestamp = TimeSpan.Zero;
        foreach (RawInputSample sample in samples)
        {
            if (sample is null)
                throw new ArgumentException("Teaching samples cannot contain null entries.", nameof(samples));
            if (sample.Timestamp < TimeSpan.Zero || sample.Timestamp < previousTimestamp)
                throw new ArgumentException("Teaching sample timestamps must be nonnegative and ordered.", nameof(samples));
            if (!double.IsFinite(sample.Value))
                throw new ArgumentException("Teaching sample values must be finite.", nameof(samples));
            if (!controls.TryGetValue(sample.ControlId, out RawControlDescriptor? control))
                throw new ArgumentException($"Sample references unknown raw control '{sample.ControlId}'.", nameof(samples));
            if (sample.Value < control.Minimum || sample.Value > control.Maximum)
                throw new ArgumentOutOfRangeException(nameof(samples), $"Sample for '{sample.ControlId}' is outside its declared range.");
            previousTimestamp = sample.Timestamp;
        }
    }

    private static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = values.Order().ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? sorted[middle - 1] / 2 + sorted[middle] / 2 : sorted[middle];
    }
}
