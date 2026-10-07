namespace ControllerOS.Core.Profiles;

public sealed record ControllerProfileMetadata(string Name, string Author = "", string Description = "");

public sealed record ControllerProfile(int SchemaVersion, ControllerProfileMetadata Metadata, string Source)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed class ControllerProfileFormatException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
