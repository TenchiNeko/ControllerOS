namespace ControllerOS.Core.ControllerScript;

public readonly record struct SourceSpan(int Line, int Column)
{
    public override string ToString() => $"{Line}:{Column}";
}

public sealed record ScriptDiagnostic(SourceSpan Span, string Message)
{
    public override string ToString() => $"{Span}: {Message}";
}

public sealed class ControllerScriptException(IReadOnlyList<ScriptDiagnostic> diagnostics)
    : Exception(string.Join(Environment.NewLine, diagnostics))
{
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; } = diagnostics;
}
