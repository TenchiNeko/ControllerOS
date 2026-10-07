using System.Diagnostics;
using System.Runtime.CompilerServices;
using ControllerOS.Cli;
using ControllerOS.Core.ControllerScript;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Devices;
using ControllerOS.Core.Output;
using ControllerOS.Core.Simulation;
using ControllerOS.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ControllerOS.Tests;

[TestClass]
public sealed class ControllerOsCliTests
{
    [TestMethod]
    public void ParserAcceptsJsonModeAndRejectsIncompleteRuntimeCommand()
    {
        ParsedCommand devices = CommandLineParser.Parse(["devices", "--json"]);
        Assert.AreEqual("devices", devices.Name);
        Assert.IsTrue(devices.Json);
        Assert.ThrowsExactly<CommandLineParseException>(() => CommandLineParser.Parse(["runtime", "start", "profile.json"]));
    }

    [TestMethod]
    public async Task DevicesCommandUsesEnumeratorAndOnlyReturnsGamepadCollections()
    {
        var enumerator = new FakeEnumerator(
        [
            Device(@"\\?\HID#VID_045E&PID_028E#private", "Controller", 0x01, 0x05),
            Device(@"\\?\HID#VID_046D&PID_C31C#keyboard", "Keyboard", 0x01, 0x06)
        ]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await ControllerOsCli.RunAsync(["devices", "--json"], TextReader.Null, output, error, enumerator);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(1, enumerator.Calls);
        StringAssert.Contains(output.ToString(), "controller-001");
        StringAssert.Contains(output.ToString(), "Controller");
        Assert.IsFalse(output.ToString().Contains("Keyboard", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains("private", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task SelfTestCommandReturnsMachineReadableSummary()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await ControllerOsCli.RunAsync(["self-test", "--json"], TextReader.Null, output, error, new FakeEnumerator([]));

        Assert.AreEqual(0, exitCode, output.ToString());
        StringAssert.Contains(output.ToString(), "runtime-and-script");
        StringAssert.Contains(output.ToString(), "synthetic-teaching");
        StringAssert.Contains(output.ToString(), "report-validation");
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task CliReportsStableUsageAndInvalidInputExitCodes()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int usageCode = await ControllerOsCli.RunAsync(["runtime", "start", "profile.json", "--json"], TextReader.Null, output, error, new FakeEnumerator([]));
        Assert.AreEqual(2, usageCode);
        StringAssert.Contains(output.ToString(), "\"exitCode\": 2");

        output.GetStringBuilder().Clear();
        int invalidInputCode = await ControllerOsCli.RunAsync(["validate", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))], TextReader.Null, output, error, new FakeEnumerator([]));
        Assert.AreEqual(3, invalidInputCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "exit 3");
    }

    [TestMethod]
    public async Task RuntimeInputPumpAdvancesScriptTimersWhileHidIsIdle()
    {
        DeviceTeachingSession teaching = CreateSyntheticTeachingSession();
        DeviceDefinition definition = teaching.Preview();
        DeviceCalibration calibration = teaching.PreviewCalibration();
        ControllerProgram program = new ControllerScriptCompiler().Compile(
            "on press(SOUTH):\n    press(EAST)\n    wait(40ms)\n    release(EAST)\n", "idle-timer-test");
        var output = new RecordedOutput();
        var runtime = new ControllerRuntime(program, output);
        runtime.Start();
        var clock = Stopwatch.StartNew();
        using var stop = new CancellationTokenSource();

        Task pump = ControllerOsCli.RunRuntimeInputAsync(
            PressThenIdle(clock, stop.Token), () => clock.Elapsed, definition, calibration, runtime, stop, TimeSpan.FromMilliseconds(5));
        await Task.Delay(200);
        stop.Cancel();
        await pump.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(output.Frames.Any(frame => frame.State.Get(ControlId.EAST).IsPressed));
        Assert.IsFalse(output.Frames[^1].State.Get(ControlId.EAST).IsPressed);
        runtime.Stop();
    }

    private static WindowsHidDevice Device(string path, string product, ushort usagePage, ushort usage) =>
        new(path, "Test Manufacturer", product, 0x045E, 0x028E, 0x0201, 0, usagePage, usage, 64, 0, 10, 6, "usb");

    private static DeviceTeachingSession CreateSyntheticTeachingSession()
    {
        var session = new DeviceTeachingSession(SyntheticUnknownDeviceFixture.Device);
        session.Observe(ControlId.SOUTH, SyntheticUnknownDeviceFixture.SouthButtonSamples());
        session.Observe(ControlId.LEFT_STICK_X, SyntheticUnknownDeviceFixture.LeftStickXSamples());
        session.Observe(ControlId.LEFT_STICK_Y, SyntheticUnknownDeviceFixture.LeftStickYSamples());
        session.Observe(ControlId.LEFT_TRIGGER, SyntheticUnknownDeviceFixture.TriggerSamples());
        foreach (ControlId control in ControlCatalog.All.Except([ControlId.SOUTH, ControlId.LEFT_STICK_X, ControlId.LEFT_STICK_Y, ControlId.LEFT_TRIGGER]))
            session.Skip(control);
        return session;
    }

    private static async IAsyncEnumerable<IReadOnlyList<RawInputSample>> PressThenIdle(
        Stopwatch clock,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return [new RawInputSample(clock.Elapsed, "button-0", 0)];
        yield return [new RawInputSample(clock.Elapsed, "button-0", 1)];
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private sealed class FakeEnumerator(IReadOnlyList<WindowsHidDevice> devices) : IWindowsDeviceEnumerator
    {
        public int Calls { get; private set; }

        public IReadOnlyList<WindowsHidDevice> Enumerate()
        {
            Calls++;
            return devices;
        }
    }
}
