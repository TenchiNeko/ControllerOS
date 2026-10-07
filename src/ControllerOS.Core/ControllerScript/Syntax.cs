using ControllerOS.Core.Controls;

namespace ControllerOS.Core.ControllerScript;

public enum ScriptEventKind
{
    Press,
    Release,
    Change
}

public sealed record ScriptProgram(
    IReadOnlyList<StateDeclaration> States,
    IReadOnlyList<FunctionDefinition> Functions,
    IReadOnlyList<EventHandlerDefinition> Handlers);

public sealed record StateDeclaration(string Name, Expression InitialValue, SourceSpan Span);
public sealed record FunctionDefinition(string Name, IReadOnlyList<string> Parameters, IReadOnlyList<Statement> Body, SourceSpan Span);
public sealed record EventHandlerDefinition(ScriptEventKind EventKind, ControlId Control, IReadOnlyList<Statement> Body, SourceSpan Span);

public abstract record Statement(SourceSpan Span);
public sealed record StateAssignment(string Name, Expression Value, SourceSpan Span) : Statement(Span);
public sealed record OutputAssignment(ControlId Control, Expression Value, SourceSpan Span) : Statement(Span);
public sealed record CallStatement(CallExpression Call, SourceSpan Span) : Statement(Span);
public sealed record IfStatement(Expression Condition, IReadOnlyList<Statement> ThenBody, IReadOnlyList<Statement> ElseBody, SourceSpan Span) : Statement(Span);
public sealed record ReturnStatement(Expression? Value, SourceSpan Span) : Statement(Span);

public abstract record Expression(SourceSpan Span);
public sealed record LiteralExpression(object Value, SourceSpan Span) : Expression(Span);
public sealed record NameExpression(string Name, SourceSpan Span) : Expression(Span);
public sealed record UnaryExpression(string Operator, Expression Operand, SourceSpan Span) : Expression(Span);
public sealed record BinaryExpression(string Operator, Expression Left, Expression Right, SourceSpan Span) : Expression(Span);
public sealed record CallExpression(string Name, IReadOnlyList<Expression> Arguments, SourceSpan Span) : Expression(Span);
