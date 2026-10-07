using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ControllerOS.Core.Devices;

public enum RawControlKind
{
    Button,
    Axis
}

public sealed record RawControlDescriptor(string Id, RawControlKind Kind, double Minimum, double Maximum);

public sealed record DeviceMatchRule(ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage)
{
    public bool Matches(DeviceMatchRule other) =>
        VendorId == other.VendorId && ProductId == other.ProductId && UsagePage == other.UsagePage && Usage == other.Usage;
}

public sealed class RawDeviceDescriptor
{
    public DeviceMatchRule Match { get; }
    public IReadOnlyList<RawControlDescriptor> Controls { get; }

    public RawDeviceDescriptor(DeviceMatchRule match, IEnumerable<RawControlDescriptor> controls)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(controls);
        if (match.VendorId == 0 || match.ProductId == 0 || match.UsagePage == 0 || match.Usage == 0)
            throw new ArgumentException("Vendor, product, usage-page, and usage identifiers must be nonzero.", nameof(match));
        Match = match;
        Controls = Array.AsReadOnly(controls.Take(129).ToArray());
        RawControlDescriptorValidator.Validate(Controls);
    }
}

public enum MappingMode
{
    Button,
    Axis,
    Trigger
}

public sealed record DeviceControlMapping(
    string RawControlId,
    ControllerOS.Core.Controls.ControlId Target,
    RawControlKind RawKind,
    MappingMode Mode,
    double RawMinimum,
    double RawMaximum);

public sealed record DeviceDefinition(
    int SchemaVersion,
    string Id,
    string DisplayName,
    DeviceMatchRule Match,
    IReadOnlyList<DeviceControlMapping> Mappings)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record DeviceDefinitionValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public static class DeviceDefinitionValidator
{
    private static readonly Regex IdPattern = new("\\A[a-z0-9][a-z0-9._-]{0,79}\\z", RegexOptions.CultureInvariant);

    public static DeviceDefinitionValidationResult Validate(DeviceDefinition? definition)
    {
        var errors = new List<string>();
        if (definition is null)
            return new(false, Array.AsReadOnly(new[] { "Definition is missing." }));

        if (definition.SchemaVersion != DeviceDefinition.CurrentSchemaVersion)
            errors.Add($"Unsupported device-definition schema version {definition.SchemaVersion}.");
        if (definition.Id is null || !IdPattern.IsMatch(definition.Id))
            errors.Add("Definition id must be a lowercase stable identifier of at most 80 characters.");
        if (string.IsNullOrWhiteSpace(definition.DisplayName) || definition.DisplayName.Length > 120 || definition.DisplayName.Any(char.IsControl))
            errors.Add("Display name must be 1..120 printable characters.");
        if (definition.Match is null)
            errors.Add("Device match rule is missing.");
        else if (definition.Match.VendorId == 0 || definition.Match.ProductId == 0 || definition.Match.UsagePage == 0 || definition.Match.Usage == 0)
            errors.Add("Vendor, product, usage-page, and usage identifiers must be nonzero.");

        if (definition.Mappings is null || definition.Mappings.Count == 0 || definition.Mappings.Count > ControllerOS.Core.Controls.ControlCatalog.All.Count)
            errors.Add("A definition must contain between one and the standard-control count of mappings.");
        else
        {
            var rawIds = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<ControllerOS.Core.Controls.ControlId>();
            foreach (DeviceControlMapping? mapping in definition.Mappings)
            {
                if (mapping is null)
                {
                    errors.Add("Mappings cannot contain null entries.");
                    continue;
                }

                if (!RawControlDescriptorValidator.IsValidId(mapping.RawControlId))
                    errors.Add($"Invalid raw control identifier '{mapping.RawControlId}'.");
                if (!Enum.IsDefined(mapping.Target))
                    errors.Add($"Invalid standard control target '{mapping.Target}'.");
                if (!Enum.IsDefined(mapping.RawKind) || !Enum.IsDefined(mapping.Mode))
                    errors.Add("Mapping kind is invalid.");
                if (!double.IsFinite(mapping.RawMinimum) || !double.IsFinite(mapping.RawMaximum) ||
                    !double.IsFinite(mapping.RawMaximum - mapping.RawMinimum) || mapping.RawMinimum >= mapping.RawMaximum)
                    errors.Add($"Raw range for '{mapping.RawControlId}' must be finite and increasing.");

                if (mapping.RawControlId is not null && !rawIds.Add(mapping.RawControlId))
                    errors.Add($"Raw control '{mapping.RawControlId}' is mapped more than once.");
                if (!targets.Add(mapping.Target))
                    errors.Add($"Standard control '{mapping.Target}' is mapped more than once.");

                if (Enum.IsDefined(mapping.Target))
                {
                    ControllerOS.Core.Controls.ControlKind targetKind = ControllerOS.Core.Controls.ControlCatalog.KindOf(mapping.Target);
                    bool compatible = targetKind switch
                    {
                        ControllerOS.Core.Controls.ControlKind.Button => mapping.Mode == MappingMode.Button && mapping.RawKind == RawControlKind.Button,
                        ControllerOS.Core.Controls.ControlKind.Axis => mapping.Mode == MappingMode.Axis && mapping.RawKind == RawControlKind.Axis,
                        ControllerOS.Core.Controls.ControlKind.Trigger => mapping.Mode == MappingMode.Trigger && mapping.RawKind == RawControlKind.Axis,
                        _ => false
                    };
                    if (!compatible)
                        errors.Add($"Mapping mode and raw kind do not match target '{mapping.Target}'.");
                }
            }
        }

        return new(errors.Count == 0, Array.AsReadOnly(errors.ToArray()));
    }
}

public static class RawControlDescriptorValidator
{
    private static readonly Regex IdPattern = new("\\A[a-z][a-z0-9_-]{0,31}\\z", RegexOptions.CultureInvariant);

    public static bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

    public static void Validate(IReadOnlyList<RawControlDescriptor> controls)
    {
        if (controls.Count == 0 || controls.Count > 128)
            throw new ArgumentException("A raw device must expose between 1 and 128 controls.", nameof(controls));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (RawControlDescriptor control in controls)
        {
            if (control is null || !IsValidId(control.Id) || !ids.Add(control.Id))
                throw new ArgumentException("Raw control identifiers must be unique safe identifiers.", nameof(controls));
            if (!Enum.IsDefined(control.Kind) || !double.IsFinite(control.Minimum) || !double.IsFinite(control.Maximum) ||
                control.Minimum >= control.Maximum || !double.IsFinite(control.Maximum - control.Minimum))
                throw new ArgumentException($"Raw control '{control.Id}' has an invalid kind or range.", nameof(controls));
        }
    }
}

public sealed class DeviceDefinitionFormatException(string message, Exception? innerException = null) : Exception(message, innerException);

public static class DeviceDefinitionJson
{
    internal const int MaximumJsonBytes = 65_536;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(DeviceDefinition definition)
    {
        DeviceDefinitionValidationResult validation = DeviceDefinitionValidator.Validate(definition);
        if (!validation.IsValid)
            throw new DeviceDefinitionFormatException(string.Join(Environment.NewLine, validation.Errors));
        return JsonSerializer.Serialize(definition, Options);
    }

    public static DeviceDefinition Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            throw new DeviceDefinitionFormatException($"Device definition JSON exceeds the {MaximumJsonBytes}-byte limit.");
        try
        {
            ValidateJsonStructure(json);
            DeviceDefinition? definition = JsonSerializer.Deserialize<DeviceDefinition>(json, Options);
            DeviceDefinitionValidationResult validation = DeviceDefinitionValidator.Validate(definition);
            if (!validation.IsValid)
                throw new DeviceDefinitionFormatException(string.Join(Environment.NewLine, validation.Errors));
            return definition!;
        }
        catch (JsonException exception)
        {
            throw new DeviceDefinitionFormatException("Device definition JSON is malformed or contains unsupported fields.", exception);
        }
    }

    internal static void ValidateJsonStructure(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            Visit(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new DeviceDefinitionFormatException("Device data JSON is malformed or too deeply nested.", exception);
        }

        static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new DeviceDefinitionFormatException($"Device data contains duplicate property '{property.Name}'.");
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    Visit(item);
            }
        }
    }

    internal static DeviceDefinition LoadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumJsonBytes)
            throw new DeviceDefinitionFormatException($"Device definition file exceeds the {MaximumJsonBytes}-byte limit.");

        byte[] buffer = new byte[MaximumJsonBytes + 1];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        if (total > MaximumJsonBytes)
            throw new DeviceDefinitionFormatException($"Device definition file exceeds the {MaximumJsonBytes}-byte limit.");

        try
        {
            return Load(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer, 0, total));
        }
        catch (DecoderFallbackException exception)
        {
            throw new DeviceDefinitionFormatException("Device definition file is not valid UTF-8.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 32,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}

public sealed record ControlCalibration(
    double Minimum,
    double Maximum,
    double Center,
    double Noise,
    int SampleCount,
    [property: JsonRequired] MappingMode Mode)
{
    public const int MaximumSampleCount = 10_000;

    public bool IsValid => Enum.IsDefined(Mode) &&
        double.IsFinite(Minimum) && double.IsFinite(Maximum) && double.IsFinite(Maximum - Minimum) &&
        double.IsFinite(Center) && double.IsFinite(Noise) && Minimum < Maximum && Minimum <= Center && Center <= Maximum && Noise >= 0 && SampleCount is > 0 and <= MaximumSampleCount &&
        (Mode switch
        {
            MappingMode.Button => Center == Minimum && Noise == 0,
            MappingMode.Axis => Center > Minimum && Center < Maximum && Noise < Math.Min(Center - Minimum, Maximum - Center),
            MappingMode.Trigger => Noise < Maximum - Minimum,
            _ => false
        });
}

public sealed record DeviceCalibration(int SchemaVersion, string CalibrationId, IReadOnlyDictionary<string, ControlCalibration> Controls)
{
    public const int CurrentSchemaVersion = 2;

    public bool IsValid => SchemaVersion == CurrentSchemaVersion &&
        Guid.TryParseExact(CalibrationId, "N", out _) &&
        Controls is not null && Controls.Count is > 0 and <= 128 &&
        Controls.All(pair => RawControlDescriptorValidator.IsValidId(pair.Key) && pair.Value is not null && pair.Value.IsValid);
}

public sealed record SavedDeviceDefinition(string DefinitionPath, string CalibrationPath);

public sealed class DeviceDefinitionStore
{
    private static readonly JsonSerializerOptions CalibrationJsonOptions = CreateCalibrationOptions();
    private readonly string root;

    public DeviceDefinitionStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.GetFullPath(root);
    }

    public SavedDeviceDefinition Save(DeviceDefinition definition, DeviceCalibration calibration)
    {
        string serializedDefinition = DeviceDefinitionJson.Serialize(definition);
        if (!calibration.IsValid)
            throw new DeviceDefinitionFormatException("Calibration data is invalid.");
        if (definition.Mappings.Any(mapping => !calibration.Controls.TryGetValue(mapping.RawControlId, out ControlCalibration? value) || value.Mode != mapping.Mode ||
                value.Minimum < mapping.RawMinimum || value.Maximum > mapping.RawMaximum) ||
            calibration.Controls.Keys.Any(id => definition.Mappings.All(mapping => mapping.RawControlId != id)))
            throw new DeviceDefinitionFormatException("Calibration controls must correspond to their reusable definition mappings and stay within their raw ranges.");

        string definitionPath = Path.Combine(root, "definitions", definition.Id + ".json");
        string calibrationPath = Path.Combine(root, "calibrations", calibration.CalibrationId + ".json");
        WriteAtomically(definitionPath, serializedDefinition);
        WriteAtomically(calibrationPath, JsonSerializer.Serialize(calibration, CalibrationJsonOptions));
        return new(definitionPath, calibrationPath);
    }

    public DeviceDefinition Load(string id)
    {
        if (!IsValidDefinitionId(id))
            throw new ArgumentException("Invalid device-definition id.", nameof(id));
        string path = Path.Combine(root, "definitions", id + ".json");
        return DeviceDefinitionJson.LoadFile(path);
    }

    public DeviceCalibration LoadCalibration(string calibrationId)
    {
        if (!Guid.TryParseExact(calibrationId, "N", out _))
            throw new ArgumentException("Invalid calibration id.", nameof(calibrationId));
        string path = Path.Combine(root, "calibrations", calibrationId + ".json");
        try
        {
            string json = ReadBoundedTextFile(path);
            DeviceDefinitionJson.ValidateJsonStructure(json);
            DeviceCalibration? calibration = JsonSerializer.Deserialize<DeviceCalibration>(json, CalibrationJsonOptions);
            if (calibration is null || !calibration.IsValid)
                throw new DeviceDefinitionFormatException("Calibration JSON is invalid or uses an unsupported version.");
            return calibration;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new DeviceDefinitionFormatException("Calibration JSON is malformed, not UTF-8, or contains unsupported fields.", exception);
        }
    }

    public DeviceDefinition? FindMatch(DeviceMatchRule match, out IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(match);
        var messages = new List<string>();
        string directory = Path.Combine(root, "definitions");
        if (!Directory.Exists(directory))
        {
            diagnostics = Array.AsReadOnly(messages.ToArray());
            return null;
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                DeviceDefinition definition = DeviceDefinitionJson.LoadFile(path);
                if (definition.Match.Matches(match))
                {
                    diagnostics = Array.AsReadOnly(messages.ToArray());
                    return definition;
                }
            }
            catch (Exception exception) when (exception is IOException or DeviceDefinitionFormatException)
            {
                messages.Add($"Skipped invalid device definition '{Path.GetFileName(path)}': {exception.Message}");
            }
        }

        diagnostics = Array.AsReadOnly(messages.ToArray());
        return null;
    }

    private static bool IsValidDefinitionId(string? id)
    {
        if (id is null || id.Length is 0 or > 80 || !(char.IsAsciiLetterLower(id[0]) || char.IsAsciiDigit(id[0])))
            return false;
        return id.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_' or '-');
    }

    private static void WriteAtomically(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
            throw new IOException("Unable to determine the device data directory.");
        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string ReadBoundedTextFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > DeviceDefinitionJson.MaximumJsonBytes)
            throw new DeviceDefinitionFormatException($"Device data file exceeds the {DeviceDefinitionJson.MaximumJsonBytes}-byte limit.");
        byte[] buffer = new byte[DeviceDefinitionJson.MaximumJsonBytes + 1];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        if (total > DeviceDefinitionJson.MaximumJsonBytes)
            throw new DeviceDefinitionFormatException($"Device data file exceeds the {DeviceDefinitionJson.MaximumJsonBytes}-byte limit.");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer, 0, total);
    }

    private static JsonSerializerOptions CreateCalibrationOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 32,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
