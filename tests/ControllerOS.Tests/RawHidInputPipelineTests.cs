using ControllerOS.Core.Controls;
using ControllerOS.Core.Devices;
using ControllerOS.Core.Reports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ControllerOS.Tests;

[TestClass]
public sealed class RawHidInputPipelineTests
{
    [TestMethod]
    public void ReplayedRawReportsUseSharedPipelineAndExistingTeachingEngine()
    {
        var decoder = new FourControlReportDecoder();
        var session = new DeviceTeachingSession(decoder.Descriptor);
        TimeSpan timestamp = TimeSpan.Zero;

        session.Observe(ControlId.SOUTH, Replay(decoder, [[0, 0, 0, 0, 255], [1, 0, 0, 0, 255], [0, 0, 0, 0, 255]], ref timestamp));
        session.Observe(ControlId.DPAD_UP, Replay(decoder, [[0, 0, 0, 0, 255], [0, 0, 0, 0, 0], [0, 0, 0, 0, 255]], ref timestamp));
        session.Observe(ControlId.LEFT_STICK_X, Replay(decoder,
            [[0, 0, 0, 0, 255], [0, 1, 0, 0, 255], [0, 0xFF, 0, 0, 255], [0, 0x81, 0, 0, 255], [0, 0x7F, 0, 0, 255], [0, 0, 0, 0, 255]], ref timestamp));
        session.Observe(ControlId.LEFT_STICK_Y, Replay(decoder,
            [[0, 0, 0, 0, 255], [0, 0, 1, 0, 255], [0, 0, 0xFF, 0, 255], [0, 0, 0x81, 0, 255], [0, 0, 0x7F, 0, 255], [0, 0, 0, 0, 255]], ref timestamp));
        session.Observe(ControlId.LEFT_TRIGGER, Replay(decoder,
            [[0, 0, 0, 0, 255], [0, 0, 0, 0, 255], [0, 0, 0, 1, 255], [0, 0, 0, 255, 255], [0, 0, 0, 0, 255]], ref timestamp));
        foreach (ControlId control in ControlCatalog.All.Except([ControlId.SOUTH, ControlId.DPAD_UP, ControlId.LEFT_STICK_X, ControlId.LEFT_STICK_Y, ControlId.LEFT_TRIGGER]))
            session.Skip(control);

        HardwareReport report = HardwareReportGenerator.Generate(session, "0.2.0-alpha.1", new string('a', 40), "contributor-tested");
        string json = HardwareReportJson.Serialize(report);

        Assert.IsTrue(session.Validate().IsValid);
        Assert.AreEqual(5, report.MappingEvidence.Count);
        Assert.AreEqual((ushort?)0x0201, report.Device.Revision);
        Assert.AreEqual("bluetooth", report.Device.ConnectionMode);
        Assert.AreEqual(new string('a', 40), report.ControllerOSCommit);
        Assert.AreEqual("contributor-tested", report.EvidenceLevel);
        Assert.IsTrue(HardwareReportJson.Deserialize(json).Validation.IsValid);
        Assert.IsTrue(RawHidInputPipeline.DecodeHatSwitch(0, 0, 7)[ControlId.DPAD_UP]);
        Assert.IsTrue(RawHidInputPipeline.DecodeHatSwitch(8, 1, 8)[ControlId.DPAD_UP]);
        Assert.IsTrue(RawHidInputPipeline.DecodeHatSwitch(null, 0, 7).Values.All(pressed => !pressed));
        foreach (string forbidden in new[]
        {
            "alice", "workstation-17", "\\\\?\\HID#VID_", "serialNumber", "192.0.2.4",
            "unrelatedHardware", "C:\\Users\\alice", "ghp_example_secret", "github_pat_example_secret", "Bearer secret"
        })
            Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Report contained '{forbidden}'.");
    }

    [TestMethod]
    public void CommonPipelineBoundsReportsAndRejectsDecoderValuesOutsideCapabilities()
    {
        var decoder = new FourControlReportDecoder();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RawHidInputPipeline.DecodeReport(decoder, new byte[RawHidInputPipeline.MaximumReportBytes + 1], TimeSpan.Zero));
        Assert.ThrowsExactly<InvalidOperationException>(() => RawHidInputPipeline.DecodeReport(new InvalidReportDecoder(decoder.Descriptor), new byte[4], TimeSpan.Zero));
    }

    private static IReadOnlyList<RawInputSample> Replay(FourControlReportDecoder decoder, byte[][] reports, ref TimeSpan timestamp)
    {
        var samples = new List<RawInputSample>();
        foreach (byte[] report in reports)
        {
            samples.AddRange(RawHidInputPipeline.DecodeReport(decoder, report, timestamp));
            timestamp += TimeSpan.FromMilliseconds(10);
        }
        return samples;
    }

    private sealed class FourControlReportDecoder : IRawHidReportDecoder
    {
        public RawDeviceDescriptor Descriptor { get; } = new(
            new DeviceMatchRule(0x045E, 0x028E, 0x0001, 0x0005),
            [
                new("button-0", RawControlKind.Button, 0, 1),
                new("button-1", RawControlKind.Button, 0, 1),
                new("button-2", RawControlKind.Button, 0, 1),
                new("button-3", RawControlKind.Button, 0, 1),
                new("button-4", RawControlKind.Button, 0, 1),
                new("axis-0", RawControlKind.Axis, -127, 127),
                new("axis-1", RawControlKind.Axis, -127, 127),
                new("axis-2", RawControlKind.Axis, 0, 255)
            ],
            0x0201,
            connectionMode: "bluetooth");

        public IReadOnlyList<RawInputSample> Decode(ReadOnlyMemory<byte> report, TimeSpan timestamp)
        {
            if (report.Length != 5)
                throw new InvalidDataException("Fixture reports contain five bytes.");
            ReadOnlySpan<byte> bytes = report.Span;
            IReadOnlyDictionary<ControlId, bool> hat = RawHidInputPipeline.DecodeHatSwitch(bytes[4] == byte.MaxValue ? null : bytes[4], 0, 7);
            return
            [
                new(timestamp, "button-0", bytes[0] & 1),
                new(timestamp, "button-1", hat[ControlId.DPAD_UP] ? 1 : 0),
                new(timestamp, "button-2", hat[ControlId.DPAD_DOWN] ? 1 : 0),
                new(timestamp, "button-3", hat[ControlId.DPAD_LEFT] ? 1 : 0),
                new(timestamp, "button-4", hat[ControlId.DPAD_RIGHT] ? 1 : 0),
                new(timestamp, "axis-0", unchecked((sbyte)bytes[1])),
                new(timestamp, "axis-1", unchecked((sbyte)bytes[2])),
                new(timestamp, "axis-2", bytes[3])
            ];
        }
    }

    private sealed class InvalidReportDecoder(RawDeviceDescriptor descriptor) : IRawHidReportDecoder
    {
        public RawDeviceDescriptor Descriptor { get; } = descriptor;
        public IReadOnlyList<RawInputSample> Decode(ReadOnlyMemory<byte> report, TimeSpan timestamp) => [new(timestamp, "axis-0", 999)];
    }
}
