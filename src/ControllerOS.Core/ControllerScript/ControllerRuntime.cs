using ControllerOS.Core.Controls;
using ControllerOS.Core.Input;
using ControllerOS.Core.Output;
using ControllerOS.Core.Runtime;

namespace ControllerOS.Core.ControllerScript;

public sealed record RuntimeDiagnostic(
    TimeSpan Timestamp,
    string Profile,
    string Handler,
    string Event,
    string Message,
    bool Disabled,
    SourceSpan? Source = null,
    Exception? Exception = null);

public sealed class ControllerRuntime
{
    public static TimeSpan TapDuration { get; } = TimeSpan.FromMilliseconds(30);

    private readonly ControllerProgram program;
    private readonly IOutputSink outputSink;
    private readonly ScriptQuotas quotas;
    private readonly DeterministicScheduler scheduler;
    private readonly Dictionary<string, CompiledFunction> functions;
    private readonly HashSet<string> disabledHandlers = new(StringComparer.Ordinal);
    private readonly List<RuntimeDiagnostic> diagnostics = [];
    private readonly Dictionary<ControlId, long> outputVersions = ControlCatalog.All.ToDictionary(id => id, _ => 0L);
    private readonly Dictionary<string, object> state;
    private long lastInputSequence = -1;
    private bool started;

    public bool IsRunning { get; private set; }
    public ControllerState InputState { get; private set; }
    public OutputState PendingOutput { get; private set; } = OutputState.Neutral;
    public IReadOnlyDictionary<string, object> State => new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(state);
    public IReadOnlyList<RuntimeDiagnostic> Diagnostics => diagnostics.AsReadOnly();
    public event Action<RuntimeDiagnostic>? Diagnostic;

    public ControllerRuntime(ControllerProgram program, IOutputSink outputSink, ControllerCapabilities? capabilities = null, ScriptQuotas? quotas = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(outputSink);
        this.program = program;
        this.outputSink = outputSink;
        this.quotas = (quotas ?? new ScriptQuotas()).Validate();
        scheduler = new DeterministicScheduler(new SchedulerQuotas(this.quotas.MaximumTasks, this.quotas.MaximumTimers, this.quotas.InstructionBudgetPerSlice));
        scheduler.Diagnostic += OnSchedulerDiagnostic;
        functions = program.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        state = new Dictionary<string, object>(program.InitialState, StringComparer.Ordinal);
        ControllerCapabilities inputCapabilities = capabilities ?? ControllerCapabilities.Standard;
        ControlId[] unsupportedControls = program.Handlers.Select(handler => handler.Control)
            .Concat(program.Handlers.SelectMany(handler => handler.Instructions)
                .Concat(program.Functions.SelectMany(function => function.Instructions))
                .Where(instruction => instruction.OpCode == ScriptOpCode.ReadControl)
                .Select(instruction => (ControlId)instruction.Operand!))
            .Distinct()
            .Where(control => !inputCapabilities.Supports(control))
            .ToArray();
        if (unsupportedControls.Length > 0)
            throw new ArgumentException($"Input capabilities do not include: {string.Join(", ", unsupportedControls)}.", nameof(capabilities));

        InputState = ControllerState.Neutral(TimeSpan.Zero, inputCapabilities);
    }

    public void Start()
    {
        if (started)
            throw new InvalidOperationException("A ControllerRuntime instance can be started only once.");
        started = true;
        IsRunning = true;
        CommitOutput(TimeSpan.Zero);
    }

    public void ProcessInputBatch(IReadOnlyList<ControllerInputEvent> inputEvents)
    {
        ArgumentNullException.ThrowIfNull(inputEvents);
        EnsureRunning();
        if (inputEvents.Count == 0)
            return;
        if (inputEvents.Count > 4096)
            throw new ArgumentOutOfRangeException(nameof(inputEvents), "One logical timestamp may contain at most 4096 input events.");

        TimeSpan timestamp = inputEvents[0].Timestamp;
        var validated = new List<(ControllerInputEvent Event, ControllerState State)>(inputEvents.Count);
        ControllerState nextState = InputState;
        long nextSequence = lastInputSequence;
        foreach (ControllerInputEvent inputEvent in inputEvents)
        {
            if (inputEvent is null)
                throw new ArgumentException("Input batches cannot contain null events.", nameof(inputEvents));
            if (inputEvent.Timestamp != timestamp || inputEvent.Timestamp < nextState.Timestamp || inputEvent.Timestamp < scheduler.Now)
                throw new ArgumentException("All events in a batch must share a nondecreasing timestamp.", nameof(inputEvents));
            if (inputEvent.Sequence <= nextSequence)
                throw new ArgumentException("Input event sequence numbers must increase.", nameof(inputEvents));
            if (!nextState.Capabilities.Supports(inputEvent.Control) || inputEvent.Current.Id != inputEvent.Control || inputEvent.Previous.Id != inputEvent.Control)
                throw new ArgumentException("Input event refers to an unsupported or mismatched control.", nameof(inputEvents));

            ControlValue current = nextState.Get(inputEvent.Control);
            if (inputEvent.Previous != current)
                throw new ArgumentException($"Input event for {inputEvent.Control} does not continue from the current state.", nameof(inputEvents));
            ControllerInputEvent? expected = ControllerInputEvent.Create(inputEvent.Timestamp, inputEvent.Sequence, inputEvent.Previous, inputEvent.Current);
            if (expected is null || expected.Kind != inputEvent.Kind)
                throw new ArgumentException("Input event kind does not match its normalized values.", nameof(inputEvents));

            nextState = nextState.WithValue(timestamp, inputEvent.Current);
            nextSequence = inputEvent.Sequence;
            validated.Add((inputEvent, nextState));
        }

        scheduler.DispatchAt(timestamp, () =>
        {
            foreach ((ControllerInputEvent inputEvent, ControllerState snapshot) in validated)
            {
                InputState = snapshot;
                DispatchEvent(inputEvent, snapshot);
            }
        }, CommitOutput);
        InputState = nextState;
        lastInputSequence = nextSequence;
    }

    public void AdvanceTo(TimeSpan timestamp)
    {
        EnsureRunning();
        scheduler.AdvanceTo(timestamp, CommitOutput);
    }

    public void Replay(IInputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureRunning();
        if (!source.Capabilities.Controls.SetEquals(InputState.Capabilities.Controls))
            throw new ArgumentException("Input source capabilities must match the runtime capabilities.", nameof(source));

        var events = new List<ControllerInputEvent>();
        foreach (ControllerInputEvent inputEvent in source.ReadEvents())
        {
            if (events.Count >= 100_000)
                throw new ArgumentException("Synthetic replay is limited to 100,000 input events.", nameof(source));
            events.Add(inputEvent);
        }

        int index = 0;
        while (index < events.Count && IsRunning)
        {
            TimeSpan timestamp = events[index].Timestamp;
            int end = index + 1;
            while (end < events.Count && events[end].Timestamp == timestamp)
                end++;
            ProcessInputBatch(events.GetRange(index, end - index));
            index = end;
        }
        if (IsRunning)
            scheduler.Complete(CommitOutput);
    }

    public void Stop()
    {
        if (!IsRunning)
            return;
        IsRunning = false;
        scheduler.CancelAll();
        ResetPendingOutput();
        CommitOutput(scheduler.Now);
    }

    internal void WriteOutput(ControlValue value)
    {
        outputVersions[value.Id]++;
        PendingOutput = PendingOutput.WithValue(value);
    }

    internal long Tap(ControlId control, string handlerName)
    {
        WriteOutput(ControlValue.Button(control, true));
        long generation = outputVersions[control];
        scheduler.ScheduleAfter(new TapReleaseTask(this, control, generation, handlerName), TapDuration);
        return generation;
    }

    internal void ReleaseTap(ControlId control, long generation)
    {
        if (outputVersions[control] != generation)
            return;
        WriteOutput(ControlValue.Button(control, false));
    }

    private void DispatchEvent(ControllerInputEvent inputEvent, ControllerState snapshot)
    {
        ScriptEventKind eventKind = inputEvent.Kind switch
        {
            InputEventKind.Press => ScriptEventKind.Press,
            InputEventKind.Release => ScriptEventKind.Release,
            _ => ScriptEventKind.Change
        };

        foreach (CompiledHandler handler in program.Handlers)
        {
            if (handler.EventKind != eventKind || handler.Control != inputEvent.Control || disabledHandlers.Contains(handler.Name))
                continue;
            try
            {
                scheduler.Schedule(new VmTask(this, handler, snapshot));
            }
            catch (SchedulerQuotaExceededException exception)
            {
                DisableHandler(handler.Name, scheduler.Now, exception.Message, exception, null);
            }
        }
    }

    private void OnSchedulerDiagnostic(SchedulerDiagnostic schedulerDiagnostic)
    {
        bool disabled = disabledHandlers.Add(schedulerDiagnostic.HandlerName);
        if (disabled)
        {
            scheduler.CancelHandlers(disabledHandlers);
            ResetPendingOutput();
        }

        SourceSpan? source = schedulerDiagnostic.Exception is VmRuntimeException runtimeException ? runtimeException.Span : null;
        string eventName = program.Handlers.FirstOrDefault(handler => handler.Name == schedulerDiagnostic.HandlerName) is { } handler
            ? $"{handler.EventKind.ToString().ToLowerInvariant()}({handler.Control})"
            : "continuation";
        PublishDiagnostic(new RuntimeDiagnostic(
            schedulerDiagnostic.Timestamp,
            program.Name,
            schedulerDiagnostic.HandlerName,
            eventName,
            schedulerDiagnostic.Message,
            disabled,
            source,
            schedulerDiagnostic.Exception));
    }

    private void DisableHandler(string handlerName, TimeSpan timestamp, string message, Exception exception, SourceSpan? source)
    {
        bool disabled = disabledHandlers.Add(handlerName);
        if (disabled)
        {
            scheduler.CancelHandlers(disabledHandlers);
            ResetPendingOutput();
        }
        CompiledHandler? handler = program.Handlers.FirstOrDefault(candidate => candidate.Name == handlerName);
        string eventName = handler is null ? "dispatch" : $"{handler.EventKind.ToString().ToLowerInvariant()}({handler.Control})";
        PublishDiagnostic(new RuntimeDiagnostic(timestamp, program.Name, handlerName, eventName, message, disabled, source, exception));
    }

    private void ResetPendingOutput()
    {
        PendingOutput = OutputState.Neutral;
        foreach (ControlId control in ControlCatalog.All)
            outputVersions[control]++;
    }

    private void CommitOutput(TimeSpan timestamp)
    {
        try
        {
            outputSink.Commit(timestamp, PendingOutput);
        }
        catch (Exception exception)
        {
            IsRunning = false;
            scheduler.CancelAll();
            ResetPendingOutput();
            PublishDiagnostic(new RuntimeDiagnostic(timestamp, program.Name, "output adapter", "commit", $"Output backend failed: {exception.Message}", true, Exception: exception));
            try
            {
                outputSink.Reset(timestamp);
            }
            catch (Exception resetException)
            {
                PublishDiagnostic(new RuntimeDiagnostic(timestamp, program.Name, "output adapter", "reset", $"Output backend could not confirm neutral state: {resetException.Message}", true, Exception: resetException));
            }
        }
    }

    private void PublishDiagnostic(RuntimeDiagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
        if (Diagnostic is null)
            return;
        foreach (Action<RuntimeDiagnostic> listener in Diagnostic.GetInvocationList().Cast<Action<RuntimeDiagnostic>>())
        {
            try
            {
                listener(diagnostic);
            }
            catch (Exception)
            {
                // A diagnostic observer cannot stop profile or input processing.
            }
        }
    }

    private void EnsureRunning()
    {
        if (!IsRunning)
            throw new InvalidOperationException("The ControllerRuntime is not running.");
    }

    private sealed class VmTask(ControllerRuntime owner, CompiledHandler handler, ControllerState initialInput) : IScheduledTask
    {
        private readonly List<Frame> frames = [new(handler.Instructions, null, [])];
        private bool hasYielded;

        public string HandlerName => handler.Name;

        public SchedulerStep RunSlice(TimeSpan now, int instructionBudget)
        {
            int executed = 0;
            while (frames.Count > 0)
            {
                Frame frame = frames[^1];
                if (executed >= instructionBudget)
                    throw new VmRuntimeException("Instruction budget exceeded.", frame.LastSpan);
                if (frame.InstructionPointer >= frame.Instructions.Count)
                    throw new VmRuntimeException("Program counter moved past the end of bytecode.", frame.LastSpan);

                ScriptInstruction instruction = frame.Instructions[frame.InstructionPointer++];
                frame.LastSpan = instruction.Span;
                executed++;
                switch (instruction.OpCode)
                {
                    case ScriptOpCode.PushConstant:
                        frame.Stack.Add(instruction.Operand!);
                        break;
                    case ScriptOpCode.ReadControl:
                        {
                            var control = (ControlId)instruction.Operand!;
                            ControllerState input = hasYielded ? owner.InputState : initialInput;
                            ControlValue value = input.Get(control);
                            frame.Stack.Add(value.Kind == ControlKind.Button ? value.IsPressed : value.Value);
                            break;
                        }
                    case ScriptOpCode.LoadState:
                        frame.Stack.Add(owner.state[(string)instruction.Operand!]);
                        break;
                    case ScriptOpCode.StoreState:
                        owner.state[(string)instruction.Operand!] = Pop(frame);
                        break;
                    case ScriptOpCode.LoadParameter:
                        frame.Stack.Add(frame.Locals[frame.ParameterSlots[(string)instruction.Operand!]]);
                        break;
                    case ScriptOpCode.WriteOutput:
                        owner.WriteOutput(ToControlValue((ControlId)instruction.Operand!, Pop(frame), instruction.Span));
                        break;
                    case ScriptOpCode.Pop:
                        _ = Pop(frame);
                        break;
                    case ScriptOpCode.Negate:
                        frame.Stack.Add(-Number(Pop(frame), instruction.Span));
                        break;
                    case ScriptOpCode.Positive:
                        frame.Stack.Add(Number(Pop(frame), instruction.Span));
                        break;
                    case ScriptOpCode.Not:
                        frame.Stack.Add(!Boolean(Pop(frame), instruction.Span));
                        break;
                    case ScriptOpCode.Add:
                    case ScriptOpCode.Subtract:
                    case ScriptOpCode.Multiply:
                    case ScriptOpCode.Divide:
                    case ScriptOpCode.Power:
                    case ScriptOpCode.Less:
                    case ScriptOpCode.LessEqual:
                    case ScriptOpCode.Greater:
                    case ScriptOpCode.GreaterEqual:
                    case ScriptOpCode.Equal:
                    case ScriptOpCode.NotEqual:
                        ApplyBinary(frame, instruction);
                        break;
                    case ScriptOpCode.Jump:
                        frame.InstructionPointer = JumpTarget(instruction);
                        break;
                    case ScriptOpCode.JumpIfFalse:
                        if (!Boolean(Pop(frame), instruction.Span))
                            frame.InstructionPointer = JumpTarget(instruction);
                        break;
                    case ScriptOpCode.CallFunction:
                        {
                            FunctionCallOperand call = (FunctionCallOperand)instruction.Operand!;
                            if (frames.Count - 1 >= owner.quotas.MaximumCallDepth)
                                throw new VmRuntimeException($"Nested call quota exceeded ({owner.quotas.MaximumCallDepth}).", instruction.Span);
                            CompiledFunction function = owner.functions[call.Name];
                            var arguments = PopArguments(frame, call.ArgumentCount, instruction.Span);
                            frames.Add(new Frame(function.Instructions, function, arguments));
                            break;
                        }
                    case ScriptOpCode.CallMath:
                        {
                            FunctionCallOperand call = (FunctionCallOperand)instruction.Operand!;
                            double[] args = PopArguments(frame, call.ArgumentCount, instruction.Span).Select(value => Number(value, instruction.Span)).ToArray();
                            frame.Stack.Add(EvaluateMath(call.Name, args, instruction.Span));
                            break;
                        }
                    case ScriptOpCode.Press:
                        owner.WriteOutput(ControlValue.Button((ControlId)instruction.Operand!, true));
                        break;
                    case ScriptOpCode.Release:
                        owner.WriteOutput(ControlValue.Button((ControlId)instruction.Operand!, false));
                        break;
                    case ScriptOpCode.Tap:
                        owner.Tap((ControlId)instruction.Operand!, handler.Name);
                        break;
                    case ScriptOpCode.Wait:
                        {
                            object durationValue = Pop(frame);
                            if (durationValue is not TimeSpan delay || delay <= TimeSpan.Zero)
                                throw new VmRuntimeException("wait() requires a positive duration.", instruction.Span);
                            hasYielded = true;
                            return SchedulerStep.Wait(delay);
                        }
                    case ScriptOpCode.Return:
                        ReturnFrame(null, hasValue: false);
                        break;
                    case ScriptOpCode.ReturnValue:
                        ReturnFrame(Pop(frame), hasValue: true);
                        break;
                    case ScriptOpCode.End:
                        if (frames.Count != 1)
                            throw new VmRuntimeException("Unexpected end of function bytecode.", instruction.Span);
                        return SchedulerStep.Complete();
                    default:
                        throw new VmRuntimeException($"Unsupported instruction {instruction.OpCode}.", instruction.Span);
                }
            }
            return SchedulerStep.Complete();

            void ReturnFrame(object? value, bool hasValue)
            {
                if (frames.Count <= 1)
                    throw new VmRuntimeException("return is invalid in an event handler.", frameSpan());
                frames.RemoveAt(frames.Count - 1);
                if (hasValue)
                    frames[^1].Stack.Add(value!);
            }

            SourceSpan frameSpan() => frames.Count > 0 ? frames[^1].LastSpan : default;
        }

        public void Cancel() => frames.Clear();

        private static object Pop(Frame frame)
        {
            if (frame.Stack.Count == 0)
                throw new VmRuntimeException("Value stack underflow.", frame.LastSpan);
            int last = frame.Stack.Count - 1;
            object value = frame.Stack[last];
            frame.Stack.RemoveAt(last);
            return value;
        }

        private static object[] PopArguments(Frame frame, int count, SourceSpan span)
        {
            var arguments = new object[count];
            for (int index = count - 1; index >= 0; index--)
                arguments[index] = Pop(frame);
            if (count < 0)
                throw new VmRuntimeException("Invalid call argument count.", span);
            return arguments;
        }

        private static int JumpTarget(ScriptInstruction instruction)
        {
            int target = (int)instruction.Operand!;
            if (target < 0)
                throw new VmRuntimeException("Invalid jump target.", instruction.Span);
            return target;
        }

        private static bool Boolean(object value, SourceSpan span) => value is bool boolean
            ? boolean
            : throw new VmRuntimeException("Expected a Boolean value.", span);

        private static double Number(object value, SourceSpan span)
        {
            if (value is not double number || !double.IsFinite(number))
                throw new VmRuntimeException("Expected a finite numeric value.", span);
            return number;
        }

        private static ControlValue ToControlValue(ControlId id, object value, SourceSpan span)
        {
            try
            {
                return ControlCatalog.KindOf(id) switch
                {
                    ControlKind.Button when value is bool boolean => ControlValue.Button(id, boolean),
                    ControlKind.Button => throw new VmRuntimeException($"Output {id} requires a Boolean value.", span),
                    ControlKind.Axis => ControlValue.Axis(id, Number(value, span)),
                    ControlKind.Trigger => ControlValue.Trigger(id, Number(value, span)),
                    _ => throw new VmRuntimeException($"Unknown output {id}.", span)
                };
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new VmRuntimeException(exception.Message, span, exception);
            }
        }

        private static double EvaluateMath(string name, double[] arguments, SourceSpan span)
        {
            double result = name switch
            {
                "abs" => Math.Abs(arguments[0]),
                "sign" => Math.Sign(arguments[0]),
                "min" => Math.Min(arguments[0], arguments[1]),
                "max" => Math.Max(arguments[0], arguments[1]),
                _ => throw new VmRuntimeException($"Unknown math function '{name}'.", span)
            };
            return Finite(result, span);
        }

        private static void ApplyBinary(Frame frame, ScriptInstruction instruction)
        {
            object right = Pop(frame);
            object left = Pop(frame);
            object result = instruction.OpCode switch
            {
                ScriptOpCode.Add => Finite(Number(left, instruction.Span) + Number(right, instruction.Span), instruction.Span),
                ScriptOpCode.Subtract => Finite(Number(left, instruction.Span) - Number(right, instruction.Span), instruction.Span),
                ScriptOpCode.Multiply => Finite(Number(left, instruction.Span) * Number(right, instruction.Span), instruction.Span),
                ScriptOpCode.Divide => Divide(left, right, instruction.Span),
                ScriptOpCode.Power => Finite(Math.Pow(Number(left, instruction.Span), Number(right, instruction.Span)), instruction.Span),
                ScriptOpCode.Equal => Equals(left, right),
                ScriptOpCode.NotEqual => !Equals(left, right),
                ScriptOpCode.Less => Number(left, instruction.Span) < Number(right, instruction.Span),
                ScriptOpCode.LessEqual => Number(left, instruction.Span) <= Number(right, instruction.Span),
                ScriptOpCode.Greater => Number(left, instruction.Span) > Number(right, instruction.Span),
                ScriptOpCode.GreaterEqual => Number(left, instruction.Span) >= Number(right, instruction.Span),
                _ => throw new VmRuntimeException($"Unsupported binary operation {instruction.OpCode}.", instruction.Span)
            };
            frame.Stack.Add(result);
        }

        private static double Divide(object left, object right, SourceSpan span)
        {
            double divisor = Number(right, span);
            if (divisor == 0.0)
                throw new VmRuntimeException("Division by zero.", span);
            return Finite(Number(left, span) / divisor, span);
        }

        private static double Finite(double value, SourceSpan span) => double.IsFinite(value)
            ? value
            : throw new VmRuntimeException("Numeric operation produced a non-finite value.", span);

        private sealed class Frame(IReadOnlyList<ScriptInstruction> instructions, CompiledFunction? function, object[] locals)
        {
            public IReadOnlyList<ScriptInstruction> Instructions { get; } = instructions;
            public object[] Locals { get; } = locals;
            public Dictionary<string, int> ParameterSlots { get; } = function?.Parameters.Select((name, index) => (name, index)).ToDictionary(pair => pair.name, pair => pair.index, StringComparer.Ordinal) ?? new Dictionary<string, int>();
            public List<object> Stack { get; } = [];
            public int InstructionPointer { get; set; }
            public SourceSpan LastSpan { get; set; }
        }
    }

    private sealed class TapReleaseTask(ControllerRuntime owner, ControlId control, long generation, string handlerName) : IScheduledTask
    {
        public string HandlerName { get; } = handlerName;
        public SchedulerStep RunSlice(TimeSpan now, int instructionBudget)
        {
            owner.ReleaseTap(control, generation);
            return SchedulerStep.Complete();
        }
        public void Cancel() { }
    }

    private sealed class VmRuntimeException(string message, SourceSpan span, Exception? inner = null)
        : Exception(message, inner)
    {
        public SourceSpan Span { get; } = span;
    }
}
