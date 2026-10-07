using ControllerOS.Core.ControllerScript;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Input;
using ControllerOS.Core.Output;
using ControllerOS.Core.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ControllerOS.Tests;

[TestClass]
public sealed class RuntimeSafetyTests
{
    [TestMethod]
    public void SchedulerRejectsMalformedZeroDelayContinuationWithoutSpinning()
    {
        var scheduler = new DeterministicScheduler();
        var diagnostics = new List<SchedulerDiagnostic>();
        var task = new InvalidDelayTask();
        scheduler.Diagnostic += diagnostics.Add;

        scheduler.DispatchAt(TimeSpan.Zero, () => scheduler.Schedule(task), _ => { });

        Assert.AreEqual(0, scheduler.ActiveTaskCount);
        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics[0].Message.Contains("positive duration", StringComparison.Ordinal));
        Assert.IsTrue(task.Cancelled);
    }

    [TestMethod]
    public void RuntimeRejectsSplitBatchesAtTheSameLogicalTimestamp()
    {
        var program = new ControllerScriptCompiler().Compile("on press(SOUTH):\n    press(EAST)\n");
        var runtime = new ControllerRuntime(program, new RecordedOutput());
        runtime.Start();
        TimeSpan timestamp = TimeSpan.FromMilliseconds(10);
        ControllerInputEvent first = ControllerInputEvent.Create(
            timestamp, 0, ControlValue.Button(ControlId.SOUTH, false), ControlValue.Button(ControlId.SOUTH, true))!;
        ControllerInputEvent second = ControllerInputEvent.Create(
            timestamp, 1, ControlValue.Button(ControlId.VIEW, false), ControlValue.Button(ControlId.VIEW, true))!;

        runtime.ProcessInputBatch([first]);

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => runtime.ProcessInputBatch([second]));
        StringAssert.Contains(error.Message, "single batch");
        Assert.IsFalse(runtime.InputState.Get(ControlId.VIEW).IsPressed);
        Assert.IsTrue(runtime.IsRunning);
    }

    [TestMethod]
    public void RuntimeAdvancesWaitsWhileInputIsIdleAndOrdersEqualTimeInputFirst()
    {
        const string source = "on press(SOUTH):\n    press(EAST)\n    wait(100ms)\n    release(EAST)\non release(SOUTH):\n    press(EAST)\n";
        var output = new RecordedOutput();
        var runtime = new ControllerRuntime(new ControllerScriptCompiler().Compile(source), output);
        runtime.Start();
        TimeSpan pressedAt = TimeSpan.FromMilliseconds(10);
        ControllerInputEvent press = ControllerInputEvent.Create(
            pressedAt, 0, ControlValue.Button(ControlId.SOUTH, false), ControlValue.Button(ControlId.SOUTH, true))!;

        runtime.ProcessInputBatch([press]);
        runtime.AdvanceTo(TimeSpan.FromMilliseconds(110));
        Assert.IsTrue(output.Frames[^1].State.Get(ControlId.EAST).IsPressed);

        TimeSpan sameDeadline = TimeSpan.FromMilliseconds(110);
        ControllerInputEvent release = ControllerInputEvent.Create(
            sameDeadline, 1, ControlValue.Button(ControlId.SOUTH, true), ControlValue.Button(ControlId.SOUTH, false))!;
        runtime.ProcessInputBatch([release]);
        Assert.IsFalse(output.Frames[^1].State.Get(ControlId.EAST).IsPressed);
        Assert.AreEqual(sameDeadline, output.Frames[^1].Timestamp);
    }

    [TestMethod]
    public void InvalidProfileCompilationDoesNotMutateAnActiveRuntime()
    {
        var output = new RecordedOutput();
        var runtime = new ControllerRuntime(
            new ControllerScriptCompiler().Compile("on press(SOUTH):\n    press(EAST)\n"), output);
        runtime.Start();

        Assert.ThrowsExactly<ControllerScriptException>(() =>
            new ControllerScriptCompiler().Compile("on press(SOUTH):\n    press(UNKNOWN)\n"));
        Assert.IsTrue(runtime.IsRunning);

        ControllerInputEvent press = ControllerInputEvent.Create(
            TimeSpan.FromMilliseconds(1), 0,
            ControlValue.Button(ControlId.SOUTH, false),
            ControlValue.Button(ControlId.SOUTH, true))!;
        runtime.ProcessInputBatch([press]);

        Assert.IsTrue(output.Frames[^1].State.Get(ControlId.EAST).IsPressed);
        Assert.IsTrue(runtime.IsRunning);
    }

    [TestMethod]
    public void RuntimeRejectsProfilesThatReadUnsupportedInputControls()
    {
        const string source = "on press(SOUTH):\n    if LEFT_TRIGGER > 0.5:\n        press(EAST)\n";
        var capabilities = new ControllerCapabilities(ControlCatalog.All.Except([ControlId.LEFT_TRIGGER]));

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => new ControllerRuntime(
            new ControllerScriptCompiler().Compile(source), new RecordedOutput(), capabilities));

        StringAssert.Contains(error.Message, nameof(ControlId.LEFT_TRIGGER));
    }

    [TestMethod]
    public void OutputCommitFailureAttemptsNeutralResetAndStopsReplaySafely()
    {
        const string source = "on press(SOUTH):\n    press(EAST)\n";
        var sink = new FailingOutputSink();
        var runtime = new ControllerRuntime(new ControllerScriptCompiler().Compile(source), sink);
        runtime.Start();
        var input = new SyntheticInput(ControllerCapabilities.Standard,
        [
            new(TimeSpan.FromMilliseconds(1), ControlValue.Button(ControlId.SOUTH, true)),
            new(TimeSpan.FromMilliseconds(2), ControlValue.Button(ControlId.SOUTH, false))
        ]);

        runtime.Replay(input);

        Assert.IsTrue(sink.ResetAttempted);
        Assert.IsFalse(sink.LastResetState!.Get(ControlId.EAST).IsPressed);
        Assert.IsFalse(runtime.IsRunning);
        Assert.IsTrue(runtime.Diagnostics.Any(diagnostic => diagnostic.Event == "commit"));
    }

    [TestMethod]
    public void CapabilityAndDeviceQuotasStopEnumerationAtLimitPlusOne()
    {
        int capabilityItems = 0;
        Assert.ThrowsExactly<ArgumentException>(() => new ControllerCapabilities(InfiniteCapabilities()));
        Assert.AreEqual(ControllerOS.Core.Controls.ControlCatalog.All.Count + 1, capabilityItems);

        int syntheticSamples = 0;
        Assert.ThrowsExactly<ArgumentException>(() => new SyntheticInput(ControllerCapabilities.Standard, InfiniteSyntheticSamples()));
        Assert.AreEqual(100_001, syntheticSamples);

        int rawControls = 0;
        Assert.ThrowsExactly<ArgumentException>(() => new ControllerOS.Core.Devices.RawDeviceDescriptor(
            new ControllerOS.Core.Devices.DeviceMatchRule(1, 2, 3, 4), InfiniteRawControls()));
        Assert.AreEqual(129, rawControls);

        int samples = 0;
        var teaching = new ControllerOS.Core.Devices.DeviceTeachingSession(ControllerOS.Core.Simulation.SyntheticUnknownDeviceFixture.Device);
        Assert.ThrowsExactly<ArgumentException>(() => teaching.Observe(ControlId.SOUTH, InfiniteSamples()));
        Assert.AreEqual(10_001, samples);

        IEnumerable<ControlId> InfiniteCapabilities()
        {
            while (true)
            {
                capabilityItems++;
                yield return ControlId.SOUTH;
            }
        }

        IEnumerable<InputSample> InfiniteSyntheticSamples()
        {
            while (true)
            {
                syntheticSamples++;
                yield return new(TimeSpan.Zero, ControlValue.Button(ControlId.SOUTH, false));
            }
        }

        IEnumerable<ControllerOS.Core.Devices.RawControlDescriptor> InfiniteRawControls()
        {
            while (true)
            {
                rawControls++;
                yield return new("axis-0", ControllerOS.Core.Devices.RawControlKind.Axis, -1, 1);
            }
        }

        IEnumerable<ControllerOS.Core.Devices.RawInputSample> InfiniteSamples()
        {
            while (true)
            {
                samples++;
                yield return new(TimeSpan.Zero, "button-0", 0);
            }
        }
    }

    [TestMethod]
    public void ScenarioCaptureBoundsCustomInputSources()
    {
        var source = new InfiniteInputSource();

        Assert.ThrowsExactly<InvalidOperationException>(() => ScenarioRunner.Capture(source));

        Assert.AreEqual(100_001, source.EventsYielded);
    }

    private sealed class InvalidDelayTask : IScheduledTask
    {
        public string HandlerName => "invalid-delay";
        public bool Cancelled { get; private set; }

        public SchedulerStep RunSlice(TimeSpan now, int instructionBudget) => new(false, TimeSpan.Zero);

        public void Cancel() => Cancelled = true;
    }

    private sealed class FailingOutputSink : IOutputSink
    {
        private int commits;
        public bool ResetAttempted { get; private set; }
        public OutputState? LastResetState { get; private set; }

        public void Commit(TimeSpan timestamp, OutputState state)
        {
            commits++;
            if (commits > 1)
                throw new IOException("simulated output failure");
        }

        public void Reset(TimeSpan timestamp)
        {
            ResetAttempted = true;
            LastResetState = OutputState.Neutral;
        }
    }

    private sealed class InfiniteInputSource : IInputSource
    {
        private bool pressed;
        public ControllerCapabilities Capabilities { get; } = ControllerCapabilities.Standard;
        public int EventsYielded { get; private set; }

        public IEnumerable<ControllerInputEvent> ReadEvents()
        {
            long sequence = 0;
            TimeSpan timestamp = TimeSpan.Zero;
            while (true)
            {
                ControlValue previous = ControlValue.Button(ControlId.SOUTH, pressed);
                ControlValue current = ControlValue.Button(ControlId.SOUTH, !pressed);
                pressed = !pressed;
                EventsYielded++;
                yield return ControllerInputEvent.Create(timestamp, sequence++, previous, current)!;
                timestamp += TimeSpan.FromTicks(1);
            }
        }
    }
}
