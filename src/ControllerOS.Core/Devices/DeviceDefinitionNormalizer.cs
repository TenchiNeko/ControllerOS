using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Devices;

public static class DeviceDefinitionNormalizer
{
    public static ControlValue Normalize(DeviceDefinition definition, DeviceCalibration calibration, string rawControlId, double rawValue)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawControlId);
        if (!double.IsFinite(rawValue))
            throw new ArgumentOutOfRangeException(nameof(rawValue), "Raw values must be finite.");

        DeviceDefinitionValidationResult validation = DeviceDefinitionValidator.Validate(definition);
        if (!validation.IsValid)
            throw new DeviceDefinitionFormatException(string.Join(Environment.NewLine, validation.Errors));
        if (!calibration.IsCompatibleWith(definition))
            throw new DeviceDefinitionFormatException("Calibration data is invalid or belongs to a different device definition.");

        DeviceControlMapping mapping = definition.Mappings.SingleOrDefault(item => item.RawControlId == rawControlId)
            ?? throw new ArgumentException($"Raw control '{rawControlId}' is not mapped.", nameof(rawControlId));
        if (!calibration.Controls.TryGetValue(rawControlId, out ControlCalibration? captured))
            throw new DeviceDefinitionFormatException($"Calibration for raw control '{rawControlId}' is missing.");
        if (!captured.IsValid || captured.Mode != mapping.Mode)
            throw new DeviceDefinitionFormatException($"Calibration for raw control '{rawControlId}' is invalid or does not match its mapping mode.");
        if (rawValue < mapping.RawMinimum || rawValue > mapping.RawMaximum ||
            captured.Minimum < mapping.RawMinimum || captured.Maximum > mapping.RawMaximum)
            throw new ArgumentOutOfRangeException(nameof(rawValue), "Raw value or calibration is outside the device definition range.");

        return mapping.Mode switch
        {
            MappingMode.Button => ControlValue.Button(mapping.Target, rawValue >= mapping.RawMinimum / 2 + mapping.RawMaximum / 2),
            MappingMode.Axis => NormalizeAxis(mapping, captured, rawValue),
            MappingMode.Trigger => NormalizeTrigger(mapping, captured, rawValue),
            _ => throw new DeviceDefinitionFormatException("Mapping mode is unsupported.")
        };
    }

    private static ControlValue NormalizeAxis(DeviceControlMapping mapping, ControlCalibration calibration, double value)
    {
        double negativeRange = calibration.Center - calibration.Minimum - calibration.Noise;
        double positiveRange = calibration.Maximum - calibration.Center - calibration.Noise;
        if (negativeRange <= 0 || positiveRange <= 0)
            throw new DeviceDefinitionFormatException($"Axis calibration for '{mapping.RawControlId}' does not include values on both sides of its center.");

        double delta = value - calibration.Center;
        double normalized = Math.Abs(delta) <= calibration.Noise
            ? 0
            : delta > 0
                ? (delta - calibration.Noise) / positiveRange
                : (delta + calibration.Noise) / negativeRange;
        return ControlValue.Axis(mapping.Target, Math.Clamp(normalized, -1, 1));
    }

    private static ControlValue NormalizeTrigger(DeviceControlMapping mapping, ControlCalibration calibration, double value)
    {
        double activeMinimum = calibration.Minimum + calibration.Noise;
        double activeRange = calibration.Maximum - activeMinimum;
        if (activeRange <= 0)
            throw new DeviceDefinitionFormatException($"Trigger calibration for '{mapping.RawControlId}' has no usable range.");
        double normalized = Math.Clamp((value - activeMinimum) / activeRange, 0, 1);
        return ControlValue.Trigger(mapping.Target, normalized);
    }
}
