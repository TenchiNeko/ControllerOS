using System.Text;
using System.Text.Json;
using ControllerOS.Core.ControllerScript;

namespace ControllerOS.Core.Profiles;

public static class ControllerProfileJson
{
    private const int MaximumJsonBytes = 500_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Serialize(ControllerProfile profile, ControllerScriptCompiler? compiler = null)
    {
        ValidateProfile(profile, compiler ?? new ControllerScriptCompiler());

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", profile.SchemaVersion);
            writer.WriteStartObject("metadata");
            writer.WriteString("name", profile.Metadata.Name);
            writer.WriteString("author", profile.Metadata.Author);
            writer.WriteString("description", profile.Metadata.Description);
            writer.WriteEndObject();
            writer.WriteString("source", profile.Source);
            writer.WriteEndObject();
        }
        return StrictUtf8.GetString(stream.ToArray());
    }

    public static ControllerProfile Deserialize(string json, ControllerScriptCompiler? compiler = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        int jsonBytes;
        try
        {
            jsonBytes = StrictUtf8.GetByteCount(json);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ControllerProfileFormatException("invalid_encoding", "Profile JSON must contain valid Unicode text.", exception);
        }
        if (jsonBytes > MaximumJsonBytes)
            throw new ControllerProfileFormatException("profile_too_large", $"Profile JSON exceeds the {MaximumJsonBytes}-byte limit.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException exception)
        {
            throw new ControllerProfileFormatException("invalid_json", $"Profile is not valid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            Dictionary<string, JsonElement> root = ReadObject(document.RootElement, "profile");
            if (!root.TryGetValue("schemaVersion", out JsonElement versionValue) ||
                versionValue.ValueKind != JsonValueKind.Number || !versionValue.TryGetInt32(out int version))
                throw Invalid("Profile must contain an integer schemaVersion.");

            if (version != ControllerProfile.CurrentSchemaVersion)
                throw new ControllerProfileFormatException("unsupported_schema_version", $"Profile schema version {version} is not supported; this build supports version {ControllerProfile.CurrentSchemaVersion}.");

            RejectUnknown(root, "profile", "schemaVersion", "metadata", "source");
            if (!root.TryGetValue("metadata", out JsonElement metadataValue))
                throw Invalid("Profile must contain metadata.");
            Dictionary<string, JsonElement> metadata = ReadObject(metadataValue, "metadata");
            RejectUnknown(metadata, "metadata", "name", "author", "description");

            string name = RequiredString(metadata, "name", "metadata");
            string author = OptionalString(metadata, "author", "metadata");
            string description = OptionalString(metadata, "description", "metadata");
            string source = RequiredString(root, "source", "profile");
            var profile = new ControllerProfile(version, new ControllerProfileMetadata(name, author, description), source);
            ValidateProfile(profile, compiler ?? new ControllerScriptCompiler());
            return profile;
        }
    }

    public static ControllerProfile LoadFile(string path, ControllerScriptCompiler? compiler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumJsonBytes)
                throw new ControllerProfileFormatException("profile_too_large", $"Profile JSON exceeds the {MaximumJsonBytes}-byte limit.");

            using var buffer = new MemoryStream((int)stream.Length);
            byte[] chunk = new byte[8192];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) != 0)
            {
                if (buffer.Length + read > MaximumJsonBytes)
                    throw new ControllerProfileFormatException("profile_too_large", $"Profile JSON exceeds the {MaximumJsonBytes}-byte limit.");
                buffer.Write(chunk, 0, read);
            }
            return Deserialize(StrictUtf8.GetString(buffer.ToArray()), compiler);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ControllerProfileFormatException("invalid_encoding", "Profile must be UTF-8 encoded.", exception);
        }
    }

    public static void SaveFile(string path, ControllerProfile profile, ControllerScriptCompiler? compiler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = StrictUtf8.GetBytes(Serialize(profile, compiler));
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void ValidateProfile(ControllerProfile profile, ControllerScriptCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion != ControllerProfile.CurrentSchemaVersion)
            throw new ControllerProfileFormatException("unsupported_schema_version", $"Profile schema version {profile.SchemaVersion} is not supported; this build supports version {ControllerProfile.CurrentSchemaVersion}.");
        if (profile.Metadata is null)
            throw Invalid("Profile metadata is required.");

        ValidateText(profile.Metadata.Name, "metadata.name", 100, required: true);
        ValidateText(profile.Metadata.Author, "metadata.author", 100, required: false);
        ValidateText(profile.Metadata.Description, "metadata.description", 1000, required: false);
        if (profile.Source is null)
            throw Invalid("Profile source is required.");
        try
        {
            _ = StrictUtf8.GetByteCount(profile.Source);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ControllerProfileFormatException("invalid_encoding", "Profile source must contain valid Unicode text.", exception);
        }

        try
        {
            compiler.Compile(profile.Source, profile.Metadata.Name);
        }
        catch (ControllerScriptException exception)
        {
            throw new ControllerProfileFormatException("invalid_script", $"Profile ControllerScript is invalid: {exception.Message}", exception);
        }
    }

    private static void ValidateText(string? value, string field, int maximumLength, bool required)
    {
        if (value is null || (required && string.IsNullOrWhiteSpace(value)))
            throw Invalid($"{field} is required.");
        if (value.Length > maximumLength)
            throw Invalid($"{field} exceeds the {maximumLength}-character limit.");
        if (value.Any(char.IsControl))
            throw Invalid($"{field} cannot contain control characters.");
        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ControllerProfileFormatException("invalid_encoding", $"{field} must contain valid Unicode text.", exception);
        }
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid($"{context} must be a JSON object.");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
                throw Invalid($"{context} contains duplicate property '{property.Name}'.");
        }
        return properties;
    }

    private static void RejectUnknown(Dictionary<string, JsonElement> properties, string context, params string[] allowed)
    {
        HashSet<string> known = new(allowed, StringComparer.Ordinal);
        string? unknown = properties.Keys.FirstOrDefault(name => !known.Contains(name));
        if (unknown is not null)
            throw Invalid($"{context} contains unsupported property '{unknown}'.");
    }

    private static string RequiredString(Dictionary<string, JsonElement> properties, string name, string context)
    {
        if (!properties.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw Invalid($"{context}.{name} must be a string.");
        return value.GetString()!;
    }

    private static string OptionalString(Dictionary<string, JsonElement> properties, string name, string context) =>
        properties.TryGetValue(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid($"{context}.{name} must be a string.")
            : string.Empty;

    private static ControllerProfileFormatException Invalid(string message) => new("invalid_profile", message);
}
