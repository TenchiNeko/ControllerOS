using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Devices;

namespace ControllerOS.Core.Reports;

public sealed record HardwareControlSummary(string Id, RawControlKind Kind, double Minimum, double Maximum);
public sealed record HardwareDeviceSummary(
    DeviceMatchRule Match,
    IReadOnlyList<HardwareControlSummary> Controls,
    ushort? Revision = null,
    string ConnectionMode = "unknown",
    string? RetailModelName = null);
public sealed record HardwareMappingEvidence(string RawControlId, ControlId Target, ControlCalibration Calibration);
public sealed record HardwareValidationSummary(bool IsValid, IReadOnlyList<string> Errors);

public static class HardwareReportIdentity
{
    private static readonly Regex RetailNamePattern = new("\\A[A-Za-z0-9][A-Za-z0-9 ._()+-]{0,119}\\z", RegexOptions.CultureInvariant);

    public static bool IsSafeRetailModelName(string value) => value is not null && RetailNamePattern.IsMatch(value) && !IPAddress.TryParse(value, out _);

    public static bool IsValidConnectionMode(string? value) => value is "usb" or "bluetooth" or "unknown";
}

public sealed record HardwareReport(
    int SchemaVersion,
    string ControllerOSVersion,
    string ControllerOSCommit,
    string EvidenceLevel,
    HardwareDeviceSummary Device,
    IReadOnlyList<ControlId> SkippedControls,
    IReadOnlyList<HardwareMappingEvidence> MappingEvidence,
    DeviceDefinition MappingCandidate,
    HardwareValidationSummary Validation)
{
    public const int CurrentSchemaVersion = 3;
}

public static class HardwareReportGenerator
{
    private static readonly Regex VersionPattern = new("\\A[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\\z", RegexOptions.CultureInvariant);

    public static HardwareReport Generate(
        DeviceTeachingSession session,
        string controllerOsVersion,
        string controllerOsCommit = "unknown",
        string evidenceLevel = "mapping-only")
    {
        ArgumentNullException.ThrowIfNull(session);
        if (controllerOsVersion is null || !VersionPattern.IsMatch(controllerOsVersion))
            throw new ArgumentException("ControllerOS version must be a semantic version such as 0.1.0-alpha.1.", nameof(controllerOsVersion));
        if (!IsValidCommit(controllerOsCommit))
            throw new ArgumentException("ControllerOS commit must be a 40..64 character hexadecimal hash or unknown.", nameof(controllerOsCommit));
        if (!IsValidEvidenceLevel(evidenceLevel))
            throw new ArgumentException("Evidence level is unsupported.", nameof(evidenceLevel));

        DeviceDefinition definition = session.Preview();
        DeviceCalibration calibration = session.PreviewCalibration();
        RawDeviceDescriptor device = session.Device;
        DeviceDefinitionValidationResult result = session.Validate();

        var reportControlIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (IGrouping<RawControlKind, RawControlDescriptor> group in device.Controls.GroupBy(control => control.Kind))
        {
            int index = 0;
            foreach (RawControlDescriptor control in group.OrderBy(control => control.Id, StringComparer.Ordinal))
            {
                string kind = group.Key == RawControlKind.Button ? "button" : "axis";
                reportControlIds.Add(control.Id, $"{kind}-{index++}");
            }
        }

        var summary = new HardwareDeviceSummary(
            device.Match,
            Array.AsReadOnly(device.Controls.OrderBy(control => control.Id, StringComparer.Ordinal)
                .Select(control => new HardwareControlSummary(reportControlIds[control.Id], control.Kind, control.Minimum, control.Maximum)).ToArray()),
            device.Revision,
            device.ConnectionMode,
            device.RetailModelName);
        var reportDefinition = definition with
        {
            Mappings = Array.AsReadOnly(definition.Mappings.Select(mapping => mapping with { RawControlId = reportControlIds[mapping.RawControlId] }).ToArray())
        };
        var evidence = definition.Mappings.OrderBy(mapping => mapping.Target)
            .Select(mapping => new HardwareMappingEvidence(reportControlIds[mapping.RawControlId], mapping.Target, calibration.Controls[mapping.RawControlId]))
            .ToArray();

        return new HardwareReport(
            SchemaVersion: HardwareReport.CurrentSchemaVersion,
            ControllerOSVersion: controllerOsVersion,
            ControllerOSCommit: controllerOsCommit,
            EvidenceLevel: evidenceLevel,
            Device: summary,
            SkippedControls: Array.AsReadOnly(session.SkippedControls.Order().ToArray()),
            MappingEvidence: Array.AsReadOnly(evidence),
            MappingCandidate: reportDefinition,
            Validation: new(result.IsValid, Array.AsReadOnly(result.Errors.ToArray())));
    }

    internal static bool IsValidCommit(string? value) => value == "unknown" || value is not null && Regex.IsMatch(value, "\\A[0-9a-f]{40,64}\\z", RegexOptions.CultureInvariant);

    internal static bool IsValidEvidenceLevel(string? value) => value is "maintainer-tested" or "contributor-tested" or "mapping-only" or "unverified";
}

public static class HardwareReportJson
{
    private static readonly Regex VersionPattern = new("\\A[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\\z", RegexOptions.CultureInvariant);
    private static readonly Regex AnonymousRawControlIdPattern = new("\\A(button|axis)-(0|[1-9][0-9]*)\\z", RegexOptions.CultureInvariant);
    private const int MaximumJsonBytes = 1_000_000;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(HardwareReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Validate(report);
        return JsonSerializer.Serialize(report, Options);
    }

    public static HardwareReport Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            throw new ArgumentException($"Hardware report exceeds the {MaximumJsonBytes}-byte limit.", nameof(json));
        DeviceDefinitionJson.ValidateJsonStructure(json);
        HardwareReport report = JsonSerializer.Deserialize<HardwareReport>(json, Options)
            ?? throw new ArgumentException("Hardware report is empty.", nameof(json));
        Validate(report);
        return report;
    }

    private static void Validate(HardwareReport report)
    {
        if (report.SchemaVersion != HardwareReport.CurrentSchemaVersion || report.ControllerOSVersion is null || report.ControllerOSVersion.Length > 64 || !VersionPattern.IsMatch(report.ControllerOSVersion) ||
            !HardwareReportGenerator.IsValidCommit(report.ControllerOSCommit) || !HardwareReportGenerator.IsValidEvidenceLevel(report.EvidenceLevel))
            throw new ArgumentException("Hardware report schema, version, commit, or evidence level is invalid.", nameof(report));
        if (report.Device?.Match is null || report.Device.Controls is null || report.Device.Controls.Count is 0 or > 128)
            throw new ArgumentException("Hardware report device summary is invalid.", nameof(report));
        if (!HardwareReportIdentity.IsValidConnectionMode(report.Device.ConnectionMode) ||
            report.Device.RetailModelName is not null && !HardwareReportIdentity.IsSafeRetailModelName(report.Device.RetailModelName))
            throw new ArgumentException("Hardware report device identity contains an unsupported model name or connection mode.", nameof(report));
        if (report.Device.Match.VendorId == 0 || report.Device.Match.ProductId == 0 || report.Device.Match.UsagePage == 0 || report.Device.Match.Usage == 0)
            throw new ArgumentException("Hardware report requires a complete stable device match rule.", nameof(report));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (HardwareControlSummary control in report.Device.Controls)
        {
            Match anonymousId = AnonymousRawControlIdPattern.Match(control?.Id ?? string.Empty);
            string expectedKind = control?.Kind == RawControlKind.Button ? "button" : "axis";
            if (control is null || !anonymousId.Success || anonymousId.Groups[1].Value != expectedKind || !ids.Add(control.Id) ||
                !Enum.IsDefined(control.Kind) || !double.IsFinite(control.Minimum) || !double.IsFinite(control.Maximum) ||
                !double.IsFinite(control.Maximum - control.Minimum) || control.Minimum >= control.Maximum)
                throw new ArgumentException("Hardware report contains an invalid raw capability.", nameof(report));
        }
        foreach (RawControlKind kind in Enum.GetValues<RawControlKind>())
        {
            string prefix = kind == RawControlKind.Button ? "button" : "axis";
            int count = report.Device.Controls.Count(control => control.Kind == kind);
            for (int ordinal = 0; ordinal < count; ordinal++)
                if (!ids.Contains($"{prefix}-{ordinal}"))
                    throw new ArgumentException("Hardware report raw control IDs must be anonymous, contiguous ordinals by kind.", nameof(report));
        }

        DeviceDefinitionValidationResult definitionValidation = DeviceDefinitionValidator.Validate(report.MappingCandidate);
        if (!definitionValidation.IsValid || report.MappingEvidence is null || report.MappingEvidence.Count > ControlCatalog.All.Count)
            throw new ArgumentException("Hardware report mapping candidate is invalid.", nameof(report));
        DeviceMatchRule match = report.Device.Match;
        string expectedId = $"hid-{match.VendorId:x4}-{match.ProductId:x4}-{match.UsagePage:x4}-{match.Usage:x4}";
        string expectedName = $"Unknown controller {match.VendorId:x4}:{match.ProductId:x4}";
        if (!report.MappingCandidate.Match.Matches(match) || report.MappingCandidate.Id != expectedId || report.MappingCandidate.DisplayName != expectedName)
            throw new ArgumentException("Hardware report mapping candidate does not match its allowlisted device identity.", nameof(report));
        if (report.Validation is null || !report.Validation.IsValid || report.Validation.Errors is null || report.Validation.Errors.Count != 0)
            throw new ArgumentException("Hardware report validation summary must describe a valid candidate.", nameof(report));
        var controlIds = report.Device.Controls.Select(control => control.Id).ToHashSet(StringComparer.Ordinal);
        var controlById = report.Device.Controls.ToDictionary(control => control.Id, StringComparer.Ordinal);
        var evidenceByTarget = new HashSet<ControlId>();
        foreach (HardwareMappingEvidence evidence in report.MappingEvidence)
        {
            if (evidence is null || !RawControlDescriptorValidator.IsValidId(evidence.RawControlId) || !Enum.IsDefined(evidence.Target) || evidence.Calibration is null ||
                !controlIds.Contains(evidence.RawControlId) || !evidenceByTarget.Add(evidence.Target) ||
                !double.IsFinite(evidence.Calibration.Minimum) || !double.IsFinite(evidence.Calibration.Maximum) ||
                !evidence.Calibration.IsValid ||
                !report.MappingCandidate.Mappings.Any(mapping => mapping.RawControlId == evidence.RawControlId && mapping.Mode == evidence.Calibration.Mode))
                throw new ArgumentException("Hardware report contains invalid mapping evidence.", nameof(report));
            HardwareControlSummary capability = controlById[evidence.RawControlId];
            if (evidence.Calibration.Minimum < capability.Minimum || evidence.Calibration.Maximum > capability.Maximum)
                throw new ArgumentException("Hardware report calibration falls outside the raw capability range.", nameof(report));
        }
        if (report.SkippedControls is null || report.SkippedControls.Count > ControlCatalog.All.Count ||
            report.SkippedControls.Any(control => !Enum.IsDefined(control)) || report.SkippedControls.Distinct().Count() != report.SkippedControls.Count ||
            report.SkippedControls.Any(control => evidenceByTarget.Contains(control)) ||
            report.SkippedControls.Count + report.MappingEvidence.Count != ControlCatalog.All.Count ||
            ControlCatalog.All.Any(control => !report.SkippedControls.Contains(control) && !evidenceByTarget.Contains(control)))
            throw new ArgumentException("Hardware report must account for every standard control as mapped or skipped exactly once.", nameof(report));
        if (report.MappingCandidate.Mappings.Count != report.MappingEvidence.Count ||
            report.MappingCandidate.Mappings.Any(mapping => !evidenceByTarget.Contains(mapping.Target) || !report.MappingEvidence.Any(evidence =>
                evidence.Target == mapping.Target && evidence.RawControlId == mapping.RawControlId)) ||
            report.MappingCandidate.Mappings.Any(mapping => !controlById.TryGetValue(mapping.RawControlId, out HardwareControlSummary? capability) ||
                capability.Kind != mapping.RawKind || capability.Minimum != mapping.RawMinimum || capability.Maximum != mapping.RawMaximum))
            throw new ArgumentException("Hardware report evidence does not cover its generated mapping candidate.", nameof(report));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 32,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
