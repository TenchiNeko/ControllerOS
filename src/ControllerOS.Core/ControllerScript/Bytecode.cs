using ControllerOS.Core.Controls;

namespace ControllerOS.Core.ControllerScript;

public enum ScriptOpCode
{
    PushConstant,
    ReadControl,
    LoadState,
    StoreState,
    LoadParameter,
    WriteOutput,
    Pop,
    Negate,
    Positive,
    Not,
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Jump,
    JumpIfFalse,
    CallFunction,
    CallMath,
    Press,
    Release,
    Tap,
    Wait,
    Return,
    ReturnValue,
    End
}

public sealed record ScriptInstruction(ScriptOpCode OpCode, object? Operand, SourceSpan Span);

internal sealed record FunctionCallOperand(string Name, int ArgumentCount);

public sealed record CompiledFunction(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<ScriptInstruction> Instructions,
    bool ReturnsValue);

public sealed record CompiledHandler(
    string Name,
    ScriptEventKind EventKind,
    ControlId Control,
    IReadOnlyList<ScriptInstruction> Instructions);

public sealed class ControllerProgram
{
    public string Name { get; }
    public IReadOnlyDictionary<string, object> InitialState { get; }
    public IReadOnlyList<CompiledFunction> Functions { get; }
    public IReadOnlyList<CompiledHandler> Handlers { get; }

    internal ControllerProgram(string name, IReadOnlyDictionary<string, object> initialState, IReadOnlyList<CompiledFunction> functions, IReadOnlyList<CompiledHandler> handlers)
    {
        Name = name;
        InitialState = initialState;
        Functions = functions;
        Handlers = handlers;
    }
}

public sealed record ScriptQuotas(
    int MaximumSourceCharacters = 65_536,
    int MaximumStateVariables = 64,
    int MaximumFunctions = 32,
    int MaximumHandlers = 128,
    int MaximumInstructions = 10_000,
    int MaximumCallDepth = 16,
    int MaximumStateStringLength = 256,
    int MaximumTasks = 64,
    int MaximumTimers = 128,
    int InstructionBudgetPerSlice = 10_000)
{
    public ScriptQuotas Validate()
    {
        if (MaximumSourceCharacters <= 0 || MaximumStateVariables <= 0 || MaximumFunctions <= 0 || MaximumHandlers <= 0 ||
            MaximumInstructions <= 0 || MaximumCallDepth <= 0 || MaximumStateStringLength <= 0 || MaximumTasks <= 0 ||
            MaximumTimers <= 0 || InstructionBudgetPerSlice <= 0)
            throw new ArgumentOutOfRangeException(nameof(ScriptQuotas), "Script quotas must be positive.");
        return this;
    }
}
