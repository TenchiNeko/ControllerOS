using ControllerOS.Core.Controls;
using ControllerOS.Core.Devices;
using ControllerOS.Core.Reports;
using ControllerOS.Core.Simulation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ControllerOS.Tests;

[TestClass]
public sealed class DeviceTeachingTests
{
    [TestMethod]
    public void VersionedDefinitionJsonValidatesAndKeepsCalibrationSeparate()
    {
        DeviceTeachingSession session = TeachSyntheticDevice();
        DeviceDefinition definition = session.Preview();
        DeviceCalibration calibration = session.PreviewCalibration();
        ControlCalibration excessiveSamples = calibration.Controls["axis-0"] with
        {
            SampleCount = ControlCalibration.MaximumSampleCount + 1
        };
        Assert.IsFalse(excessiveSamples.IsValid);
        var excessiveSampleControls = calibration.Controls.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        excessiveSampleControls["axis-0"] = excessiveSamples;
        Assert.IsFalse((calibration with
        {
            Controls = excessiveSampleControls
        }).IsValid);

        string json = DeviceDefinitionJson.Serialize(definition);
        DeviceDefinition loaded = DeviceDefinitionJson.Load(json);

        Assert.AreEqual(DeviceDefinition.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(definition.Id, loaded.Id);
        Assert.AreEqual(4, loaded.Mappings.Count);
        Assert.IsFalse(json.Contains("calibration", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(calibration.Controls.ContainsKey("axis-0"));
        Assert.IsTrue(DeviceDefinitionValidator.Validate(loaded).IsValid);
        DeviceDefinition hugeRange = loaded with
        {
            Mappings = Array.AsReadOnly(loaded.Mappings.Select((mapping, index) => index == 0
                ? mapping with { RawMinimum = -double.MaxValue, RawMaximum = double.MaxValue }
                : mapping).ToArray())
        };
        Assert.IsFalse(DeviceDefinitionValidator.Validate(hugeRange).IsValid);

        string unknownVersion = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999", StringComparison.Ordinal);
        Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => DeviceDefinitionJson.Load(unknownVersion));
        Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => DeviceDefinitionJson.Load("{\"schemaVersion\":1,\"unexpected\":true}"));
        string duplicateProperty = json.Replace("\"productId\": 22136", "\"productId\": 22136, \"productId\": 22136", StringComparison.Ordinal);
        Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => DeviceDefinitionJson.Load(duplicateProperty));
        Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => DeviceDefinitionJson.Load(new string(' ', 70_000)));
        Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => DeviceDefinitionJson.Load("{\"schemaVersion\":1,\"nested\":" + new string('[', 40) + "0" + new string(']', 40) + "}"));
    }

    [TestMethod]
    public void SyntheticTeachingIdentifiesButtonsAxesTriggersCapturesNoiseAndSkipsUnsupportedControls()
    {
        DeviceTeachingSession session = TeachSyntheticDevice();

        Assert.IsTrue(session.IsComplete);
        Assert.AreEqual(4, session.IdentifiedControls.Count);
        Assert.AreEqual(ControlCatalog.All.Count - 4, session.SkippedControls.Count);

        DeviceDefinition candidate = session.Preview();
        Assert.AreEqual("button-0", candidate.Mappings.Single(mapping => mapping.Target == ControlId.SOUTH).RawControlId);
        Assert.AreEqual("axis-0", candidate.Mappings.Single(mapping => mapping.Target == ControlId.LEFT_STICK_X).RawControlId);
        Assert.AreEqual("axis-1", candidate.Mappings.Single(mapping => mapping.Target == ControlId.LEFT_STICK_Y).RawControlId);
        Assert.AreEqual("axis-2", candidate.Mappings.Single(mapping => mapping.Target == ControlId.LEFT_TRIGGER).RawControlId);

        DeviceCalibration calibration = session.PreviewCalibration();
        Assert.AreEqual(10.0, calibration.Controls["axis-0"].Center);
        Assert.AreEqual(2.0, calibration.Controls["axis-0"].Noise);
        Assert.AreEqual(-1000.0, calibration.Controls["axis-0"].Minimum);
        Assert.AreEqual(1000.0, calibration.Controls["axis-0"].Maximum);
        Assert.AreEqual(0.0, calibration.Controls["axis-2"].Center);
        Assert.AreEqual(1.0, calibration.Controls["axis-2"].Noise);
        Assert.IsTrue(session.Validate().IsValid);

        Assert.AreEqual(-1.0, DeviceDefinitionNormalizer.Normalize(candidate, calibration, "axis-0", -1000).Value);
        Assert.AreEqual(0.0, DeviceDefinitionNormalizer.Normalize(candidate, calibration, "axis-0", 10).Value);
        Assert.AreEqual(1.0, DeviceDefinitionNormalizer.Normalize(candidate, calibration, "axis-0", 1000).Value);
        Assert.AreEqual(0.0, DeviceDefinitionNormalizer.Normalize(candidate, calibration, "axis-2", 0).Value);
        Assert.AreEqual(1.0, DeviceDefinitionNormalizer.Normalize(candidate, calibration, "axis-2", 255).Value);
        Assert.IsTrue(DeviceDefinitionNormalizer.Normalize(candidate, calibration, "button-0", 1).IsPressed);
    }

    [TestMethod]
    public void LocalSaveLoadsDefinitionAndRecognizesDeviceWithoutEmbeddingUnitCalibration()
    {
        DeviceTeachingSession session = TeachSyntheticDevice();
        string root = Path.Combine(Path.GetTempPath(), "controlleros-device-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DeviceDefinitionStore(root);
            DeviceDefinition outOfRangeMapping = session.Preview() with
            {
                Mappings = Array.AsReadOnly(session.Preview().Mappings.Select(mapping => mapping.Target == ControlId.LEFT_STICK_X
                    ? mapping with { RawMinimum = -999 }
                    : mapping).ToArray())
            };
            Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => store.Save(outOfRangeMapping, session.PreviewCalibration()));
            DeviceCalibration currentCalibration = session.PreviewCalibration();
            var excessiveSampleControls = currentCalibration.Controls.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            excessiveSampleControls["axis-0"] = excessiveSampleControls["axis-0"] with
            {
                SampleCount = ControlCalibration.MaximumSampleCount + 1
            };
            DeviceCalibration excessiveSamples = currentCalibration with { Controls = excessiveSampleControls };
            Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => store.Save(session.Preview(), excessiveSamples));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "definitions")));
            SavedDeviceDefinition saved = session.Save(store);
            DeviceDefinition loaded = store.Load(session.Preview().Id);
            DeviceCalibration calibration = store.LoadCalibration(session.PreviewCalibration().CalibrationId);
            string definitionsDirectory = Path.GetDirectoryName(saved.DefinitionPath)!;
            File.WriteAllText(Path.Combine(definitionsDirectory, "000-too-large.json"), new string(' ', 70_000));
            DeviceDefinition? recognized = store.FindMatch(SyntheticUnknownDeviceFixture.Device.Match, out IReadOnlyList<string> diagnostics);

            Assert.AreEqual(session.Preview().Id, loaded.Id);
            Assert.IsNotNull(recognized);
            Assert.AreEqual(4, calibration.Controls.Count);
            Assert.AreEqual(1, diagnostics.Count);
            StringAssert.Contains(diagnostics[0], "byte limit");
            Assert.IsTrue(File.Exists(saved.DefinitionPath));
            Assert.IsTrue(File.Exists(saved.CalibrationPath));
            Assert.IsNull(store.FindMatch(new DeviceMatchRule(0x4321, 0x8765, 1, 5), out _));
            Assert.ThrowsExactly<ArgumentException>(() => store.Load("../outside"));

            string calibrationJson = File.ReadAllText(saved.CalibrationPath);
            string version = $"\"schemaVersion\": {DeviceCalibration.CurrentSchemaVersion}";
            File.WriteAllText(saved.CalibrationPath, calibrationJson.Replace(version, $"{version}, {version}", StringComparison.Ordinal));
            Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => store.LoadCalibration(session.PreviewCalibration().CalibrationId));

            File.WriteAllText(saved.CalibrationPath, calibrationJson.Replace("\"noise\": 2", "\"noise\": 1000", StringComparison.Ordinal));
            Assert.ThrowsExactly<DeviceDefinitionFormatException>(() => store.LoadCalibration(session.PreviewCalibration().CalibrationId));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void HardwareReportContainsAllowlistedEvidenceAndNoHostOrUserFields()
    {
        DeviceTeachingSession session = TeachSyntheticDevice();
        HardwareReport report = HardwareReportGenerator.Generate(session, "0.1.0-alpha.1");
        string json = HardwareReportJson.Serialize(report);

        Assert.AreEqual("0.1.0-alpha.1", report.ControllerOSVersion);
        Assert.AreEqual(4, report.MappingEvidence.Count);
        Assert.AreEqual(HardwareReport.CurrentSchemaVersion, report.SchemaVersion);
        Assert.IsTrue(report.Validation.IsValid);
        Assert.IsTrue(json.Contains("mappingCandidate", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("vendorId", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"target\": \"lefT_STICK_X\"", StringComparison.Ordinal), json);

        HardwareReport ReplaceButtonId(string replacement) => report with
        {
            Device = report.Device with
            {
                Controls = Array.AsReadOnly(report.Device.Controls.Select(control => control.Id == "button-0"
                    ? control with { Id = replacement }
                    : control).ToArray())
            },
            MappingEvidence = Array.AsReadOnly(report.MappingEvidence.Select(evidence => evidence.RawControlId == "button-0"
                ? evidence with { RawControlId = replacement }
                : evidence).ToArray()),
            MappingCandidate = report.MappingCandidate with
            {
                Mappings = Array.AsReadOnly(report.MappingCandidate.Mappings.Select(mapping => mapping.RawControlId == "button-0"
                    ? mapping with { RawControlId = replacement }
                    : mapping).ToArray())
            }
        };
        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportJson.Serialize(ReplaceButtonId("alice")));
        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportJson.Serialize(ReplaceButtonId("button-1")));
        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportJson.Serialize(report with
        {
            MappingEvidence = Array.AsReadOnly(report.MappingEvidence.Select(evidence => evidence.Target == ControlId.LEFT_STICK_X
                ? evidence with { Calibration = evidence.Calibration with { SampleCount = ControlCalibration.MaximumSampleCount + 1 } }
                : evidence).ToArray())
        }));

        foreach (string forbidden in new[]
        {
            "userName", "computerName", "serialNumber", "unrelatedDevices", "ipAddress", "filePath",
            "C:\\Users\\alice", "alice", "workstation-17", "192.0.2.4"
        })
            Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Report contained '{forbidden}'.");

        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportGenerator.Generate(session, "not-a-version"));
        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportJson.Serialize(report with { ControllerOSVersion = new string('1', 100) }));
        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportGenerator.Generate(session, "0.1.0-alpha.1\n"));
        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportJson.Serialize(report with
        {
            Device = report.Device with { Match = new DeviceMatchRule(0, 0, 0, 0) }
        }));
        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportJson.Serialize(report with
        {
            MappingCandidate = report.MappingCandidate with { DisplayName = "alice workstation" }
        }));
        Assert.ThrowsExactly<ArgumentException>(() => HardwareReportJson.Serialize(report with
        {
            Device = report.Device with
            {
                Controls = Array.AsReadOnly(report.Device.Controls.Select((control, index) => index == 0
                ? control with { Minimum = -double.MaxValue, Maximum = double.MaxValue }
                : control).ToArray())
            }
        }));
        Assert.ThrowsExactly<ArgumentException>(() => new RawDeviceDescriptor(
            SyntheticUnknownDeviceFixture.Device.Match,
            [new("C:\\Users\\alice\\hid", RawControlKind.Axis, -1, 1)]));
    }

    [TestMethod]
    public void HardwareReportReplacesArbitraryRawControlLabelsWithAnonymousIds()
    {
        var device = new RawDeviceDescriptor(new DeviceMatchRule(0x1234, 0x5678, 1, 5),
        [
            new("alice", RawControlKind.Button, 0, 1),
            new("workstation-17", RawControlKind.Axis, -100, 100),
            new("path-users-home", RawControlKind.Axis, -100, 100),
            new("ip-192-0-2-4", RawControlKind.Axis, 0, 255)
        ]);
        var session = new DeviceTeachingSession(device);
        session.Observe(ControlId.SOUTH,
        [
            new(TimeSpan.Zero, "alice", 0),
            new(TimeSpan.FromMilliseconds(1), "alice", 1)
        ]);
        session.Observe(ControlId.LEFT_STICK_X,
        [
            new(TimeSpan.Zero, "workstation-17", 0),
            new(TimeSpan.FromMilliseconds(1), "workstation-17", 0),
            new(TimeSpan.FromMilliseconds(2), "workstation-17", 0),
            new(TimeSpan.FromMilliseconds(3), "workstation-17", -100),
            new(TimeSpan.FromMilliseconds(4), "workstation-17", 100)
        ]);
        session.Observe(ControlId.LEFT_STICK_Y,
        [
            new(TimeSpan.Zero, "path-users-home", 0),
            new(TimeSpan.FromMilliseconds(1), "path-users-home", 0),
            new(TimeSpan.FromMilliseconds(2), "path-users-home", 0),
            new(TimeSpan.FromMilliseconds(3), "path-users-home", -100),
            new(TimeSpan.FromMilliseconds(4), "path-users-home", 100)
        ]);
        session.Observe(ControlId.LEFT_TRIGGER,
        [
            new(TimeSpan.Zero, "ip-192-0-2-4", 0),
            new(TimeSpan.FromMilliseconds(1), "ip-192-0-2-4", 0),
            new(TimeSpan.FromMilliseconds(2), "ip-192-0-2-4", 0),
            new(TimeSpan.FromMilliseconds(3), "ip-192-0-2-4", 255)
        ]);
        foreach (ControlId control in ControlCatalog.All.Except(
            [ControlId.SOUTH, ControlId.LEFT_STICK_X, ControlId.LEFT_STICK_Y, ControlId.LEFT_TRIGGER]))
            session.Skip(control);

        string json = HardwareReportJson.Serialize(HardwareReportGenerator.Generate(session, "0.1.0-alpha.1"));

        foreach (string privateLabel in new[] { "alice", "workstation-17", "path-users-home", "ip-192-0-2-4" })
            Assert.IsFalse(json.Contains(privateLabel, StringComparison.OrdinalIgnoreCase), $"Report retained raw label '{privateLabel}'.");
        Assert.IsTrue(json.Contains("button-0", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("axis-0", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TeachingRejectsAmbiguousMovementAndInvalidRawSamples()
    {
        var session = new DeviceTeachingSession(SyntheticUnknownDeviceFixture.Device);
        RawInputSample[] ambiguous = SyntheticUnknownDeviceFixture.LeftStickXSamples()
            .Concat(SyntheticUnknownDeviceFixture.LeftStickYSamples())
            .OrderBy(sample => sample.Timestamp)
            .ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Observe(ControlId.LEFT_STICK_X, ambiguous));

        RawInputSample[] outOfRange = [new(TimeSpan.Zero, "axis-0", 0), new(TimeSpan.FromMilliseconds(1), "axis-0", 3000)];
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.Observe(ControlId.LEFT_STICK_X, outOfRange));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Preview());
    }

    private static DeviceTeachingSession TeachSyntheticDevice()
    {
        var session = new DeviceTeachingSession(SyntheticUnknownDeviceFixture.Device);
        session.Observe(ControlId.SOUTH, SyntheticUnknownDeviceFixture.SouthButtonSamples());
        session.Observe(ControlId.LEFT_STICK_X, SyntheticUnknownDeviceFixture.LeftStickXSamples());
        session.Observe(ControlId.LEFT_STICK_Y, SyntheticUnknownDeviceFixture.LeftStickYSamples());
        session.Observe(ControlId.LEFT_TRIGGER, SyntheticUnknownDeviceFixture.TriggerSamples());
        foreach (ControlId control in ControlCatalog.All.Where(control =>
            control != ControlId.SOUTH && control != ControlId.LEFT_STICK_X && control != ControlId.LEFT_STICK_Y && control != ControlId.LEFT_TRIGGER))
            session.Skip(control);
        return session;
    }
}
