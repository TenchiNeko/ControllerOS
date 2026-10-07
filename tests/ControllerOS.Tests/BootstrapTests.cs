using System.Reflection;
using ControllerOS.Core.ControllerScript;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Input;
using ControllerOS.Core.Output;
using ControllerOS.Core.Runtime;
using ControllerOS.Core.Simulation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ControllerOS.Tests;

[TestClass]
public sealed class BootstrapTests
{
    [TestMethod]
    public void PortableCoreProjectCanBeLoaded()
    {
        Assembly coreAssembly = Assembly.Load("ControllerOS.Core");

        Assert.AreEqual("ControllerOS.Core", coreAssembly.GetName().Name);
    }

    [TestMethod]
    public void NormalizedValuesEnforceSemanticRanges()
    {
        Assert.AreEqual(ControlKind.Button, ControlCatalog.KindOf(ControlId.SOUTH));
        Assert.AreEqual(ControlKind.Axis, ControlCatalog.KindOf(ControlId.LEFT_STICK_X));
        Assert.AreEqual(ControlKind.Trigger, ControlCatalog.KindOf(ControlId.LEFT_TRIGGER));
        Assert.AreEqual(-1.0, ControlValue.Axis(ControlId.LEFT_STICK_X, -1.0).Value);
        Assert.AreEqual(1.0, ControlValue.Trigger(ControlId.RIGHT_TRIGGER, 1.0).Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ControlValue.Axis(ControlId.LEFT_STICK_X, 1.01));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ControlValue.Trigger(ControlId.LEFT_TRIGGER, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ControlValue.FromNormalized(ControlId.SOUTH, 0.5));
    }

    [TestMethod]
    public void StateTransitionsAndSerializationAreDeterministic()
    {
        var source = SyntheticDeviceFixtures.StandardButtonPress();
        ScenarioResult first = ScenarioRunner.Capture(source);
        ScenarioResult second = ScenarioRunner.Capture(source);

        Assert.AreEqual(2, first.Events.Count);
        Assert.AreEqual(InputEventKind.Press, first.Events[0].Kind);
        Assert.AreEqual(InputEventKind.Release, first.Events[1].Kind);
        Assert.AreEqual(TimeSpan.FromMilliseconds(30), first.FinalState.Timestamp);
        Assert.IsFalse(first.FinalState.Get(ControlId.SOUTH).IsPressed);
        Assert.AreEqual(StateJson.Serialize(first.FinalState), StateJson.Serialize(second.FinalState));
    }

    [TestMethod]
    public void CapabilitiesRejectUnsupportedControlsAndFixturesReplayNoisyAndOffCenterAxes()
    {
        ControllerState state = ControllerState.Neutral(TimeSpan.Zero, SyntheticDeviceFixtures.MissingTriggers);
        Assert.ThrowsExactly<InvalidOperationException>(() => state.Get(ControlId.LEFT_TRIGGER));
        Assert.AreEqual(4, ScenarioRunner.Capture(SyntheticDeviceFixtures.NoisyStick()).Events.Count);
        Assert.AreEqual(3, ScenarioRunner.Capture(SyntheticDeviceFixtures.OffCenterStick()).Events.Count);
    }

    [TestMethod]
    public void RecordedOutputKeepsTimestampedSnapshotsInCommitOrder()
    {
        var output = new RecordedOutput();
        output.Commit(TimeSpan.FromMilliseconds(4), OutputState.Neutral.WithValue(ControlValue.Button(ControlId.EAST, true)));
        output.Reset(TimeSpan.FromMilliseconds(8));

        Assert.AreEqual(2, output.Frames.Count);
        Assert.AreEqual(0L, output.Frames[0].Sequence);
        Assert.IsTrue(output.Frames[0].State.Get(ControlId.EAST).IsPressed);
        Assert.IsFalse(output.Frames[1].State.Get(ControlId.EAST).IsPressed);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => output.Commit(TimeSpan.FromMilliseconds(7), OutputState.Neutral));
    }

    [TestMethod]
    public void SchedulerKeepsInputMovingAndOrdersSameTimeWorkDeterministically()
    {
        var scheduler = new DeterministicScheduler();
        var trace = new List<string>();
        scheduler.DispatchAt(TimeSpan.Zero, () =>
        {
            scheduler.Schedule(new TraceTask("first", trace, wait: TimeSpan.FromMilliseconds(10)));
            scheduler.Schedule(new TraceTask("second", trace, wait: TimeSpan.FromMilliseconds(10)));
        }, _ => trace.Add("commit-0"));

        scheduler.DispatchAt(TimeSpan.FromMilliseconds(5), () =>
        {
            trace.Add("input-5");
            scheduler.Schedule(new TraceTask("input-handler", trace));
        }, _ => trace.Add("commit-5"));

        CollectionAssert.AreEqual(new[] { "first", "second", "commit-0", "input-5", "input-handler", "commit-5" }, trace);

        scheduler.DispatchAt(TimeSpan.FromMilliseconds(10), () =>
        {
            trace.Add("input-10");
            scheduler.Schedule(new TraceTask("input-at-timer", trace));
        }, _ => trace.Add("commit-10"));

        CollectionAssert.AreEqual(
            new[] { "first", "second", "commit-0", "input-5", "input-handler", "commit-5", "input-10", "input-at-timer", "first-resume", "second-resume", "commit-10" },
            trace);
    }

    [TestMethod]
    public void SchedulerIsolatesTimerQuotaFailuresAndCanCancelPendingWork()
    {
        var scheduler = new DeterministicScheduler(new SchedulerQuotas(MaximumTasks: 4, MaximumTimers: 1, InstructionBudgetPerSlice: 7));
        var trace = new List<string>();
        var diagnostics = new List<SchedulerDiagnostic>();
        scheduler.Diagnostic += diagnostics.Add;
        scheduler.DispatchAt(TimeSpan.Zero, () =>
        {
            scheduler.Schedule(new TraceTask("kept", trace, wait: TimeSpan.FromMilliseconds(1)));
            scheduler.Schedule(new TraceTask("quota", trace, wait: TimeSpan.FromMilliseconds(1)));
        }, _ => { });

        Assert.AreEqual(1, scheduler.ActiveTaskCount);
        Assert.AreEqual(1, scheduler.ActiveTimerCount);
        Assert.AreEqual(1, diagnostics.Count);
        StringAssert.Contains(diagnostics[0].Message, "timer quota");
        Assert.AreEqual("kept", trace[0]);
        Assert.AreEqual(7, TraceTask.LastInstructionBudget);

        scheduler.CancelAll();
        Assert.AreEqual(0, scheduler.ActiveTaskCount);
        Assert.AreEqual(0, scheduler.ActiveTimerCount);
    }

    [TestMethod]
    public void MinimumControllerScriptCompilesToDeterministicInstructions()
    {
        const string source = """
            state enabled = true

            on press(SOUTH):
                if enabled and LEFT_TRIGGER > 0.5:
                    press(EAST)
                    wait(100ms)
                    release(EAST)
            """;

        ControllerProgram program = new ControllerScriptCompiler().Compile(source, "conformance");

        Assert.AreEqual("conformance", program.Name);
        Assert.AreEqual(true, program.InitialState["enabled"]);
        Assert.AreEqual(1, program.Handlers.Count);
        CollectionAssert.AreEqual(
            new[] { ScriptOpCode.LoadState, ScriptOpCode.JumpIfFalse, ScriptOpCode.ReadControl },
            program.Handlers[0].Instructions.Take(3).Select(instruction => instruction.OpCode).ToArray());
        Assert.IsTrue(program.Handlers[0].Instructions.Any(instruction => instruction.OpCode == ScriptOpCode.Wait));
    }

    [TestMethod]
    public void ControllerScriptFunctionsAndInvalidControlsAreChecked()
    {
        const string source = """
            def precision(x):
                return sign(x) * abs(x) ** 1.5

            on change(RIGHT_STICK_X):
                output.RIGHT_STICK_X = precision(RIGHT_STICK_X)
            """;

        ControllerProgram program = new ControllerScriptCompiler().Compile(source);
        Assert.AreEqual(1, program.Functions.Count);
        Assert.IsTrue(program.Functions[0].ReturnsValue);
        Assert.ThrowsExactly<ControllerScriptException>(() => new ControllerScriptCompiler().Compile("on press(LEFT_STICK_X):\n    press(EAST)\n"));
        Assert.ThrowsExactly<ControllerScriptException>(() => new ControllerScriptCompiler().Compile("on change(MAGIC):\n    press(EAST)\n"));

        ControllerProgram aliasProgram = new ControllerScriptCompiler().Compile("on change(RIGHT_X):\n    output.RIGHT_X = RIGHT_X\n");
        Assert.AreEqual(ControlId.RIGHT_STICK_X, aliasProgram.Handlers[0].Control);
        Assert.AreEqual(ControlId.RIGHT_STICK_X, (ControlId)aliasProgram.Handlers[0].Instructions[1].Operand!);
    }

    [TestMethod]
    public void ControllerScriptDiagnosticsKeepSourceLocationsAndRejectRecursion()
    {
        ControllerScriptException syntax = Assert.ThrowsExactly<ControllerScriptException>(
            () => new ControllerScriptCompiler().Compile("on press(SOUTH):\n    123\n"));
        Assert.AreEqual(new SourceSpan(2, 5), syntax.Diagnostics[0].Span);

        ControllerScriptException recursion = Assert.ThrowsExactly<ControllerScriptException>(() =>
            new ControllerScriptCompiler().Compile("def first(x):\n    return second(x)\ndef second(x):\n    return first(x)\non press(SOUTH):\n    press(EAST)\n"));
        Assert.IsTrue(recursion.Message.Contains("Recursive function calls", StringComparison.Ordinal));

        ControllerScriptException hugeDuration = Assert.ThrowsExactly<ControllerScriptException>(() =>
            new ControllerScriptCompiler().Compile("on press(SOUTH):\n    wait(999999999999999999999999999999999999ms)\n"));
        Assert.IsTrue(hugeDuration.Diagnostics[0].Message.Contains("Duration", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SyntheticSouthPressRunsScriptWaitAndRecordedOutputEndToEnd()
    {
        const string source = """
            state enabled = true

            on press(SOUTH):
                if enabled and LEFT_TRIGGER > 0.5:
                    press(EAST)
                    wait(100ms)
                    release(EAST)
            """;
        var input = new SyntheticInput(ControllerCapabilities.Standard,
        [
            new(TimeSpan.Zero, ControlValue.Trigger(ControlId.LEFT_TRIGGER, 0.8)),
            new(TimeSpan.FromMilliseconds(10), ControlValue.Button(ControlId.SOUTH, true)),
            new(TimeSpan.FromMilliseconds(30), ControlValue.Button(ControlId.SOUTH, false))
        ]);
        var output = new RecordedOutput();
        var runtime = new ControllerRuntime(new ControllerScriptCompiler().Compile(source), output);
        runtime.Start();
        runtime.Replay(input);

        OutputFrame pressed = output.Frames.Single(frame => frame.Timestamp == TimeSpan.FromMilliseconds(10));
        OutputFrame released = output.Frames.Single(frame => frame.Timestamp == TimeSpan.FromMilliseconds(110));
        Assert.IsTrue(pressed.State.Get(ControlId.EAST).IsPressed);
        Assert.IsFalse(released.State.Get(ControlId.EAST).IsPressed);
        Assert.AreEqual(0, runtime.Diagnostics.Count);
    }

    [TestMethod]
    public void WaitLeavesRuntimeAvailableForUnrelatedInputAndCommitsSameTimeEventsAtomically()
    {
        const string source = """
            on press(SOUTH):
                press(EAST)
                wait(100ms)
                release(EAST)

            on press(NORTH):
                press(WEST)

            on release(SOUTH):
                release(EAST)
            """;
        var input = new SyntheticInput(ControllerCapabilities.Standard,
        [
            new(TimeSpan.FromMilliseconds(1), ControlValue.Button(ControlId.SOUTH, true)),
            new(TimeSpan.FromMilliseconds(20), ControlValue.Button(ControlId.NORTH, true)),
            new(TimeSpan.FromMilliseconds(20), ControlValue.Button(ControlId.SOUTH, false))
        ]);
        var output = new RecordedOutput();
        var runtime = new ControllerRuntime(new ControllerScriptCompiler().Compile(source), output);
        runtime.Start();
        runtime.Replay(input);

        OutputFrame at20 = output.Frames.Single(frame => frame.Timestamp == TimeSpan.FromMilliseconds(20));
        Assert.IsFalse(at20.State.Get(ControlId.EAST).IsPressed);
        Assert.IsTrue(at20.State.Get(ControlId.WEST).IsPressed);
        Assert.AreEqual(1, output.Frames.Count(frame => frame.Timestamp == TimeSpan.FromMilliseconds(20)));
        Assert.IsTrue(output.Frames.Any(frame => frame.Timestamp == TimeSpan.FromMilliseconds(101) && frame.State.Get(ControlId.EAST).IsPressed == false));
    }

    [TestMethod]
    public void RuntimeQuotaDisablesOnlyTheFailingHandlerAndReleasesOutput()
    {
        const string source = """
            on press(SOUTH):
                press(EAST)
                press(NORTH)
                press(WEST)

            on press(VIEW):
                press(DPAD_UP)
            """;
        var input = new SyntheticInput(ControllerCapabilities.Standard,
        [
            new(TimeSpan.FromMilliseconds(1), ControlValue.Button(ControlId.SOUTH, true)),
            new(TimeSpan.FromMilliseconds(2), ControlValue.Button(ControlId.VIEW, true))
        ]);
        var output = new RecordedOutput();
        var runtime = new ControllerRuntime(new ControllerScriptCompiler().Compile(source), output, quotas: new ScriptQuotas(InstructionBudgetPerSlice: 3));
        runtime.Start();
        runtime.Replay(input);

        Assert.AreEqual(1, runtime.Diagnostics.Count);
        Assert.IsTrue(runtime.Diagnostics[0].Disabled);
        Assert.IsTrue(runtime.Diagnostics[0].Message.Contains("Instruction budget", StringComparison.Ordinal));
        OutputFrame afterFailure = output.Frames.Single(frame => frame.Timestamp == TimeSpan.FromMilliseconds(2));
        Assert.IsTrue(afterFailure.State.Get(ControlId.DPAD_UP).IsPressed);
        Assert.IsTrue(runtime.IsRunning);
    }

    private sealed class TraceTask(string name, List<string> trace, TimeSpan? wait = null) : IScheduledTask
    {
        private bool resumed;
        public static int LastInstructionBudget { get; private set; }
        public string HandlerName => name;

        public SchedulerStep RunSlice(TimeSpan now, int instructionBudget)
        {
            LastInstructionBudget = instructionBudget;
            if (wait is not null && !resumed)
            {
                trace.Add(name);
                resumed = true;
                return SchedulerStep.Wait(wait.Value);
            }

            trace.Add(resumed ? $"{name}-resume" : name);
            return SchedulerStep.Complete();
        }

        public void Cancel() => trace.Add($"{name}-cancel");
    }
}
