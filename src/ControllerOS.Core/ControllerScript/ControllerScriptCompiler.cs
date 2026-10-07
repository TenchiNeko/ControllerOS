using System.Collections.ObjectModel;
using ControllerOS.Core.Controls;

namespace ControllerOS.Core.ControllerScript;

public sealed class ControllerScriptCompiler(ScriptQuotas? quotas = null)
{
    private readonly ScriptQuotas limits = (quotas ?? new ScriptQuotas()).Validate();

    public ControllerProgram Compile(string source, string profileName = "inline")
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > limits.MaximumSourceCharacters)
            throw new ControllerScriptException([new ScriptDiagnostic(new SourceSpan(1, 1), $"Source exceeds the {limits.MaximumSourceCharacters}-character limit.")]);

        ScriptProgram syntax = new Parser(new Lexer(source).Lex()).Parse();
        return new Compilation(syntax, limits, profileName).Compile();
    }

    private sealed class Compilation(ScriptProgram syntax, ScriptQuotas limits, string profileName)
    {
        private readonly List<ScriptDiagnostic> diagnostics = [];
        private readonly Dictionary<string, StateDeclaration> states = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScriptType> stateTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> initialState = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FunctionDefinition> functions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScriptType> functionReturnTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> inferringFunctions = new(StringComparer.Ordinal);
        private int instructionCount;

        public ControllerProgram Compile()
        {
            BuildSymbols();
            if (diagnostics.Count > 0)
                throw new ControllerScriptException(diagnostics.AsReadOnly());

            DetectRecursion();
            foreach (string functionName in functions.Keys)
                InferFunctionReturnType(functionName);

            var compiledFunctions = new List<CompiledFunction>();
            foreach (FunctionDefinition function in syntax.Functions)
            {
                var parameterTypes = function.Parameters.ToDictionary(name => name, _ => ScriptType.Number, StringComparer.Ordinal);
                ScriptType returnType = functionReturnTypes.GetValueOrDefault(function.Name, ScriptType.Void);
                ValidateStatements(function.Body, parameterTypes, inHandler: false, returnType);
                var builder = new BytecodeBuilder(this);
                CompileStatements(function.Body, parameterTypes, builder);
                if (!DefinitelyReturns(function.Body))
                    builder.Emit(ScriptOpCode.Return, null, function.Span);
                compiledFunctions.Add(new CompiledFunction(function.Name, function.Parameters, builder.ToReadOnly(), returnType != ScriptType.Void));
            }

            var compiledHandlers = new List<CompiledHandler>();
            int handlerIndex = 0;
            foreach (EventHandlerDefinition handler in syntax.Handlers)
            {
                string handlerName = $"{handler.EventKind.ToString().ToLowerInvariant()}({handler.Control})#{handlerIndex++}";
                if (handler.EventKind is ScriptEventKind.Press or ScriptEventKind.Release && ControlCatalog.KindOf(handler.Control) != ControlKind.Button)
                    AddError(handler.Span, $"'{handler.EventKind.ToString().ToLowerInvariant()}' requires a button control; {handler.Control} is a {ControlCatalog.KindOf(handler.Control).ToString().ToLowerInvariant()}.");

                ValidateStatements(handler.Body, EmptyParameters, inHandler: true, ScriptType.Void);
                var builder = new BytecodeBuilder(this);
                CompileStatements(handler.Body, EmptyParameters, builder);
                builder.Emit(ScriptOpCode.End, null, handler.Span);
                compiledHandlers.Add(new CompiledHandler(handlerName, handler.EventKind, handler.Control, builder.ToReadOnly()));
            }

            if (instructionCount > limits.MaximumInstructions)
                AddError(new SourceSpan(1, 1), $"Compiled program exceeds the {limits.MaximumInstructions}-instruction limit.");
            if (diagnostics.Count > 0)
                throw new ControllerScriptException(diagnostics.AsReadOnly());

            return new ControllerProgram(
                profileName,
                new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(initialState, StringComparer.Ordinal)),
                compiledFunctions.AsReadOnly(),
                compiledHandlers.AsReadOnly());
        }

        private static IReadOnlyDictionary<string, ScriptType> EmptyParameters { get; } = new Dictionary<string, ScriptType>();

        private void BuildSymbols()
        {
            if (syntax.States.Count > limits.MaximumStateVariables)
                AddError(new SourceSpan(1, 1), $"Profile exceeds the {limits.MaximumStateVariables}-state-variable limit.");
            if (syntax.Functions.Count > limits.MaximumFunctions)
                AddError(new SourceSpan(1, 1), $"Profile exceeds the {limits.MaximumFunctions}-function limit.");
            if (syntax.Handlers.Count > limits.MaximumHandlers)
                AddError(new SourceSpan(1, 1), $"Profile exceeds the {limits.MaximumHandlers}-handler limit.");

            foreach (StateDeclaration state in syntax.States)
            {
                if (!IsValidName(state.Name) || IsReserved(state.Name))
                {
                    AddError(state.Span, $"'{state.Name}' is reserved or invalid as a state name.");
                    continue;
                }
                if (states.ContainsKey(state.Name))
                {
                    AddError(state.Span, $"State '{state.Name}' is declared more than once.");
                    continue;
                }
                if (state.InitialValue is not LiteralExpression literal)
                {
                    AddError(state.InitialValue.Span, "State values must start with a literal in v0.1.");
                    continue;
                }

                ScriptType type = TypeOfLiteral(literal.Value);
                if (literal.Value is string value && value.Length > limits.MaximumStateStringLength)
                {
                    AddError(state.Span, $"State strings are limited to {limits.MaximumStateStringLength} characters.");
                    continue;
                }
                states.Add(state.Name, state);
                stateTypes.Add(state.Name, type);
                initialState.Add(state.Name, literal.Value);
            }

            foreach (FunctionDefinition function in syntax.Functions)
            {
                if (!IsValidName(function.Name) || IsReserved(function.Name) || states.ContainsKey(function.Name))
                {
                    AddError(function.Span, $"'{function.Name}' is reserved or invalid as a function name.");
                    continue;
                }
                if (functions.ContainsKey(function.Name))
                {
                    AddError(function.Span, $"Function '{function.Name}' is declared more than once.");
                    continue;
                }
                if (function.Parameters.Count > limits.MaximumCallDepth || function.Parameters.Count > 16)
                    AddError(function.Span, "Functions may have at most 16 numeric parameters.");
                if (function.Parameters.Distinct(StringComparer.Ordinal).Count() != function.Parameters.Count)
                    AddError(function.Span, "Function parameter names must be unique.");
                foreach (string parameter in function.Parameters)
                {
                    if (!IsValidName(parameter) || IsReserved(parameter) || states.ContainsKey(parameter))
                        AddError(function.Span, $"'{parameter}' is reserved or invalid as a parameter name.");
                }
                functions.Add(function.Name, function);
            }

            foreach (EventHandlerDefinition handler in syntax.Handlers)
            {
                if (!Enum.IsDefined(handler.Control))
                    AddError(handler.Span, "Handler refers to an invalid control.");
            }
        }

        private void DetectRecursion()
        {
            var graph = functions.Keys.ToDictionary(name => name, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
            foreach ((string name, FunctionDefinition function) in functions)
            {
                VisitCalls(function.Body, call =>
                {
                    if (graph.ContainsKey(call.Name))
                        graph[name].Add(call.Name);
                });
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var path = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in graph.Keys)
                Visit(name);

            void Visit(string name)
            {
                if (visited.Contains(name))
                    return;
                if (!path.Add(name))
                {
                    AddError(functions[name].Span, $"Recursive function calls are not allowed ('{name}').");
                    return;
                }
                foreach (string next in graph[name])
                {
                    if (path.Contains(next))
                        AddError(functions[next].Span, $"Recursive function calls are not allowed ('{next}').");
                    else
                        Visit(next);
                }
                path.Remove(name);
                visited.Add(name);
            }
        }

        private ScriptType InferFunctionReturnType(string name)
        {
            if (functionReturnTypes.TryGetValue(name, out ScriptType cached))
                return cached;
            if (!functions.TryGetValue(name, out FunctionDefinition? function))
                return ScriptType.Unknown;
            if (!inferringFunctions.Add(name))
                return ScriptType.Unknown;

            var parameterTypes = function.Parameters.ToDictionary(parameter => parameter, _ => ScriptType.Number, StringComparer.Ordinal);
            var returnTypes = new List<(ScriptType Type, SourceSpan Span)>();
            VisitReturns(function.Body, statement =>
            {
                if (statement.Value is null)
                    returnTypes.Add((ScriptType.Void, statement.Span));
                else
                    returnTypes.Add((InferExpression(statement.Value, parameterTypes), statement.Span));
            });

            ScriptType returnType = returnTypes.Count == 0 ? ScriptType.Void : returnTypes[0].Type;
            foreach ((ScriptType actual, SourceSpan span) in returnTypes)
            {
                if (actual != returnType)
                    AddError(span, $"All return statements in '{name}' must return the same type.");
                if (actual is not ScriptType.Number and not ScriptType.Void)
                    AddError(span, $"Function '{name}' may return only a number or no value in v0.1.");
            }
            if (returnType != ScriptType.Void && !DefinitelyReturns(function.Body))
                AddError(function.Span, $"Function '{name}' must return a value on every path.");

            inferringFunctions.Remove(name);
            functionReturnTypes[name] = returnType;
            return returnType;
        }

        private void ValidateStatements(IReadOnlyList<Statement> statements, IReadOnlyDictionary<string, ScriptType> parameters, bool inHandler, ScriptType expectedReturn)
        {
            foreach (Statement statement in statements)
            {
                switch (statement)
                {
                    case StateAssignment assignment:
                        if (!stateTypes.TryGetValue(assignment.Name, out ScriptType stateType))
                            AddError(assignment.Span, $"Unknown state variable '{assignment.Name}'. Only declared state values can be assigned.");
                        else
                        {
                            ScriptType actual = InferExpression(assignment.Value, parameters);
                            RequireType(actual, stateType, assignment.Value.Span, $"Assignment to state '{assignment.Name}'");
                            if (assignment.Value is LiteralExpression { Value: string value } && value.Length > limits.MaximumStateStringLength)
                                AddError(assignment.Span, $"State strings are limited to {limits.MaximumStateStringLength} characters.");
                        }
                        break;
                    case OutputAssignment output:
                        {
                            ScriptType expected = ControlCatalog.KindOf(output.Control) == ControlKind.Button ? ScriptType.Bool : ScriptType.Number;
                            ScriptType actual = InferExpression(output.Value, parameters);
                            RequireType(actual, expected, output.Value.Span, $"Output {output.Control}");
                            break;
                        }
                    case CallStatement call:
                        ValidateCallStatement(call.Call, parameters);
                        break;
                    case IfStatement conditional:
                        RequireType(InferExpression(conditional.Condition, parameters), ScriptType.Bool, conditional.Condition.Span, "if condition");
                        ValidateStatements(conditional.ThenBody, parameters, inHandler, expectedReturn);
                        ValidateStatements(conditional.ElseBody, parameters, inHandler, expectedReturn);
                        break;
                    case ReturnStatement result:
                        if (inHandler)
                            AddError(result.Span, "return is only valid inside a function.");
                        ScriptType returnType = result.Value is null ? ScriptType.Void : InferExpression(result.Value, parameters);
                        if (expectedReturn != ScriptType.Unknown)
                            RequireType(returnType, expectedReturn, result.Span, "return value");
                        break;
                }
            }
        }

        private void ValidateCallStatement(CallExpression call, IReadOnlyDictionary<string, ScriptType> parameters)
        {
            if (call.Name is "press" or "release" or "tap")
            {
                if (call.Arguments.Count != 1 || call.Arguments[0] is not NameExpression controlName || !ControlCatalog.TryParse(controlName.Name, out ControlId control))
                {
                    AddError(call.Span, $"{call.Name}() requires one button control name, such as {call.Name}(EAST).");
                    return;
                }
                if (ControlCatalog.KindOf(control) != ControlKind.Button)
                    AddError(call.Span, $"{call.Name}() requires a button; {control} is not a button.");
                return;
            }
            if (call.Name == "wait")
            {
                if (call.Arguments.Count != 1)
                {
                    AddError(call.Span, "wait() requires one duration, such as wait(100ms).");
                    return;
                }
                RequireType(InferExpression(call.Arguments[0], parameters), ScriptType.Duration, call.Arguments[0].Span, "wait() argument");
                if (call.Arguments[0] is LiteralExpression { Value: TimeSpan duration } && duration <= TimeSpan.Zero)
                    AddError(call.Arguments[0].Span, "wait() duration must be positive.");
                return;
            }

            ScriptType result = InferCall(call, parameters);
            if (result == ScriptType.Unknown)
                return;
        }

        private ScriptType InferExpression(Expression expression, IReadOnlyDictionary<string, ScriptType> parameters)
        {
            switch (expression)
            {
                case LiteralExpression literal:
                    return TypeOfLiteral(literal.Value);
                case NameExpression name:
                    if (parameters.TryGetValue(name.Name, out ScriptType parameterType))
                        return parameterType;
                    if (ControlCatalog.TryParse(name.Name, out ControlId control))
                        return ControlCatalog.KindOf(control) == ControlKind.Button ? ScriptType.Bool : ScriptType.Number;
                    if (stateTypes.TryGetValue(name.Name, out ScriptType stateType))
                        return stateType;
                    AddError(name.Span, $"Unknown value '{name.Name}'.");
                    return ScriptType.Unknown;
                case UnaryExpression unary:
                    {
                        ScriptType operand = InferExpression(unary.Operand, parameters);
                        ScriptType expected = unary.Operator == "not" ? ScriptType.Bool : ScriptType.Number;
                        RequireType(operand, expected, unary.Operand.Span, $"Unary '{unary.Operator}' operand");
                        return expected;
                    }
                case BinaryExpression binary:
                    return InferBinary(binary, parameters);
                case CallExpression call:
                    return InferCall(call, parameters);
                default:
                    return ScriptType.Unknown;
            }
        }

        private ScriptType InferBinary(BinaryExpression binary, IReadOnlyDictionary<string, ScriptType> parameters)
        {
            ScriptType left = InferExpression(binary.Left, parameters);
            ScriptType right = InferExpression(binary.Right, parameters);
            switch (binary.Operator)
            {
                case "and":
                case "or":
                    RequireType(left, ScriptType.Bool, binary.Left.Span, $"'{binary.Operator}' operand");
                    RequireType(right, ScriptType.Bool, binary.Right.Span, $"'{binary.Operator}' operand");
                    return ScriptType.Bool;
                case "==":
                case "!=":
                    if (left != ScriptType.Unknown && right != ScriptType.Unknown && left != right)
                        AddError(binary.Span, $"Cannot compare {left} with {right}.");
                    return ScriptType.Bool;
                case "<":
                case "<=":
                case ">":
                case ">=":
                    RequireType(left, ScriptType.Number, binary.Left.Span, "Comparison operand");
                    RequireType(right, ScriptType.Number, binary.Right.Span, "Comparison operand");
                    return ScriptType.Bool;
                default:
                    RequireType(left, ScriptType.Number, binary.Left.Span, "Arithmetic operand");
                    RequireType(right, ScriptType.Number, binary.Right.Span, "Arithmetic operand");
                    return ScriptType.Number;
            }
        }

        private ScriptType InferCall(CallExpression call, IReadOnlyDictionary<string, ScriptType> parameters)
        {
            if (call.Name is "abs" or "sign")
            {
                RequireArgumentCount(call, 1);
                foreach (Expression argument in call.Arguments)
                    RequireType(InferExpression(argument, parameters), ScriptType.Number, argument.Span, $"{call.Name}() argument");
                return ScriptType.Number;
            }
            if (call.Name is "min" or "max")
            {
                RequireArgumentCount(call, 2);
                foreach (Expression argument in call.Arguments)
                    RequireType(InferExpression(argument, parameters), ScriptType.Number, argument.Span, $"{call.Name}() argument");
                return ScriptType.Number;
            }
            if (call.Name is "press" or "release" or "tap" or "wait")
            {
                AddError(call.Span, $"{call.Name}() is a statement and cannot be used as a value.");
                return ScriptType.Unknown;
            }
            if (functions.TryGetValue(call.Name, out FunctionDefinition? function))
            {
                RequireArgumentCount(call, function.Parameters.Count);
                foreach (Expression argument in call.Arguments)
                    RequireType(InferExpression(argument, parameters), ScriptType.Number, argument.Span, $"Argument to {call.Name}()");
                ScriptType returnType = InferFunctionReturnType(call.Name);
                if (returnType == ScriptType.Void)
                    AddError(call.Span, $"Function '{call.Name}' does not return a value.");
                return returnType;
            }
            AddError(call.Span, $"Unknown function '{call.Name}'.");
            return ScriptType.Unknown;
        }

        private void RequireArgumentCount(CallExpression call, int expected)
        {
            if (call.Arguments.Count != expected)
                AddError(call.Span, $"{call.Name}() expects {expected} argument(s), received {call.Arguments.Count}.");
        }

        private void RequireType(ScriptType actual, ScriptType expected, SourceSpan span, string context)
        {
            if (actual != ScriptType.Unknown && actual != expected)
                AddError(span, $"{context} requires {expected}, got {actual}.");
        }

        private void CompileStatements(IReadOnlyList<Statement> statements, IReadOnlyDictionary<string, ScriptType> parameters, BytecodeBuilder builder)
        {
            foreach (Statement statement in statements)
            {
                switch (statement)
                {
                    case StateAssignment assignment:
                        CompileExpression(assignment.Value, parameters, builder);
                        builder.Emit(ScriptOpCode.StoreState, assignment.Name, assignment.Span);
                        break;
                    case OutputAssignment output:
                        CompileExpression(output.Value, parameters, builder);
                        builder.Emit(ScriptOpCode.WriteOutput, output.Control, output.Span);
                        break;
                    case CallStatement call:
                        CompileCallStatement(call.Call, parameters, builder);
                        break;
                    case IfStatement conditional:
                        {
                            CompileExpression(conditional.Condition, parameters, builder);
                            int falseJump = builder.EmitJump(ScriptOpCode.JumpIfFalse, conditional.Span);
                            CompileStatements(conditional.ThenBody, parameters, builder);
                            if (conditional.ElseBody.Count > 0)
                            {
                                int endJump = builder.EmitJump(ScriptOpCode.Jump, conditional.Span);
                                builder.PatchJump(falseJump, builder.Count);
                                CompileStatements(conditional.ElseBody, parameters, builder);
                                builder.PatchJump(endJump, builder.Count);
                            }
                            else
                                builder.PatchJump(falseJump, builder.Count);
                            break;
                        }
                    case ReturnStatement result:
                        if (result.Value is null)
                            builder.Emit(ScriptOpCode.Return, null, result.Span);
                        else
                        {
                            CompileExpression(result.Value, parameters, builder);
                            builder.Emit(ScriptOpCode.ReturnValue, null, result.Span);
                        }
                        break;
                }
            }
        }

        private void CompileCallStatement(CallExpression call, IReadOnlyDictionary<string, ScriptType> parameters, BytecodeBuilder builder)
        {
            if (call.Name is "press" or "release" or "tap")
            {
                if (call.Arguments.Count == 1 && call.Arguments[0] is NameExpression name && ControlCatalog.TryParse(name.Name, out ControlId control))
                    builder.Emit(call.Name switch { "press" => ScriptOpCode.Press, "release" => ScriptOpCode.Release, _ => ScriptOpCode.Tap }, control, call.Span);
                return;
            }
            if (call.Name == "wait")
            {
                if (call.Arguments.Count == 1)
                {
                    CompileExpression(call.Arguments[0], parameters, builder);
                    builder.Emit(ScriptOpCode.Wait, null, call.Span);
                }
                return;
            }

            if (IsMathFunction(call.Name))
            {
                CompileArguments(call.Arguments, parameters, builder);
                builder.Emit(ScriptOpCode.CallMath, new FunctionCallOperand(call.Name, call.Arguments.Count), call.Span);
                builder.Emit(ScriptOpCode.Pop, null, call.Span);
            }
            else if (functions.TryGetValue(call.Name, out FunctionDefinition? function))
            {
                CompileArguments(call.Arguments, parameters, builder);
                builder.Emit(ScriptOpCode.CallFunction, new FunctionCallOperand(call.Name, function.Parameters.Count), call.Span);
                if (functionReturnTypes.GetValueOrDefault(call.Name) != ScriptType.Void)
                    builder.Emit(ScriptOpCode.Pop, null, call.Span);
            }
        }

        private void CompileExpression(Expression expression, IReadOnlyDictionary<string, ScriptType> parameters, BytecodeBuilder builder)
        {
            switch (expression)
            {
                case LiteralExpression literal:
                    builder.Emit(ScriptOpCode.PushConstant, literal.Value, literal.Span);
                    break;
                case NameExpression name:
                    if (parameters.TryGetValue(name.Name, out _))
                        builder.Emit(ScriptOpCode.LoadParameter, name.Name, name.Span);
                    else if (ControlCatalog.TryParse(name.Name, out ControlId control))
                        builder.Emit(ScriptOpCode.ReadControl, control, name.Span);
                    else
                        builder.Emit(ScriptOpCode.LoadState, name.Name, name.Span);
                    break;
                case UnaryExpression unary:
                    CompileExpression(unary.Operand, parameters, builder);
                    builder.Emit(unary.Operator switch { "not" => ScriptOpCode.Not, "-" => ScriptOpCode.Negate, _ => ScriptOpCode.Positive }, null, unary.Span);
                    break;
                case BinaryExpression { Operator: "and" } binary:
                    {
                        CompileExpression(binary.Left, parameters, builder);
                        int falseJump = builder.EmitJump(ScriptOpCode.JumpIfFalse, binary.Span);
                        CompileExpression(binary.Right, parameters, builder);
                        int endJump = builder.EmitJump(ScriptOpCode.Jump, binary.Span);
                        builder.PatchJump(falseJump, builder.Count);
                        builder.Emit(ScriptOpCode.PushConstant, false, binary.Span);
                        builder.PatchJump(endJump, builder.Count);
                        break;
                    }
                case BinaryExpression { Operator: "or" } binary:
                    {
                        CompileExpression(binary.Left, parameters, builder);
                        int rightJump = builder.EmitJump(ScriptOpCode.JumpIfFalse, binary.Span);
                        builder.Emit(ScriptOpCode.PushConstant, true, binary.Span);
                        int endJump = builder.EmitJump(ScriptOpCode.Jump, binary.Span);
                        builder.PatchJump(rightJump, builder.Count);
                        CompileExpression(binary.Right, parameters, builder);
                        builder.PatchJump(endJump, builder.Count);
                        break;
                    }
                case BinaryExpression binary:
                    CompileExpression(binary.Left, parameters, builder);
                    CompileExpression(binary.Right, parameters, builder);
                    builder.Emit(BinaryOpCode(binary.Operator), null, binary.Span);
                    break;
                case CallExpression call:
                    CompileArguments(call.Arguments, parameters, builder);
                    if (IsMathFunction(call.Name))
                        builder.Emit(ScriptOpCode.CallMath, new FunctionCallOperand(call.Name, call.Arguments.Count), call.Span);
                    else
                        builder.Emit(ScriptOpCode.CallFunction, new FunctionCallOperand(call.Name, call.Arguments.Count), call.Span);
                    break;
            }
        }

        private void CompileArguments(IReadOnlyList<Expression> arguments, IReadOnlyDictionary<string, ScriptType> parameters, BytecodeBuilder builder)
        {
            foreach (Expression argument in arguments)
                CompileExpression(argument, parameters, builder);
        }

        private void AddError(SourceSpan span, string message) => diagnostics.Add(new ScriptDiagnostic(span, message));

        public void CountInstruction() => instructionCount++;

        private static ScriptType TypeOfLiteral(object value) => value switch
        {
            bool => ScriptType.Bool,
            double => ScriptType.Number,
            TimeSpan => ScriptType.Duration,
            string => ScriptType.String,
            _ => ScriptType.Unknown
        };

        private static bool IsValidName(string name) => name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

        private static bool IsReserved(string name) => ControlCatalog.TryParse(name, out _) || name is "state" or "def" or "on" or "if" or "else" or "return" or "true" or "false" or "and" or "or" or "not" or "output" or "press" or "release" or "tap" or "wait" or "abs" or "sign" or "min" or "max";

        private static bool IsMathFunction(string name) => name is "abs" or "sign" or "min" or "max";

        private static ScriptOpCode BinaryOpCode(string op) => op switch
        {
            "+" => ScriptOpCode.Add,
            "-" => ScriptOpCode.Subtract,
            "*" => ScriptOpCode.Multiply,
            "/" => ScriptOpCode.Divide,
            "**" => ScriptOpCode.Power,
            "==" => ScriptOpCode.Equal,
            "!=" => ScriptOpCode.NotEqual,
            "<" => ScriptOpCode.Less,
            "<=" => ScriptOpCode.LessEqual,
            ">" => ScriptOpCode.Greater,
            ">=" => ScriptOpCode.GreaterEqual,
            _ => throw new InvalidOperationException($"Unknown binary operator '{op}'.")
        };

        private static bool DefinitelyReturns(IReadOnlyList<Statement> statements) => statements.Any(statement => statement switch
        {
            ReturnStatement => true,
            IfStatement conditional when conditional.ElseBody.Count > 0 =>
                DefinitelyReturns(conditional.ThenBody) && DefinitelyReturns(conditional.ElseBody),
            _ => false
        });

        private static void VisitReturns(IReadOnlyList<Statement> statements, Action<ReturnStatement> visitor)
        {
            foreach (Statement statement in statements)
            {
                if (statement is ReturnStatement result)
                    visitor(result);
                else if (statement is IfStatement conditional)
                {
                    VisitReturns(conditional.ThenBody, visitor);
                    VisitReturns(conditional.ElseBody, visitor);
                }
            }
        }

        private static void VisitCalls(IReadOnlyList<Statement> statements, Action<CallExpression> visitor)
        {
            foreach (Statement statement in statements)
            {
                switch (statement)
                {
                    case StateAssignment assignment: VisitCalls(assignment.Value, visitor); break;
                    case OutputAssignment output: VisitCalls(output.Value, visitor); break;
                    case CallStatement call: VisitCalls(call.Call, visitor); break;
                    case IfStatement conditional:
                        VisitCalls(conditional.Condition, visitor);
                        VisitCalls(conditional.ThenBody, visitor);
                        VisitCalls(conditional.ElseBody, visitor);
                        break;
                    case ReturnStatement { Value: not null } result: VisitCalls(result.Value, visitor); break;
                }
            }
        }

        private static void VisitCalls(Expression expression, Action<CallExpression> visitor)
        {
            switch (expression)
            {
                case CallExpression call:
                    visitor(call);
                    foreach (Expression argument in call.Arguments)
                        VisitCalls(argument, visitor);
                    break;
                case UnaryExpression unary: VisitCalls(unary.Operand, visitor); break;
                case BinaryExpression binary:
                    VisitCalls(binary.Left, visitor);
                    VisitCalls(binary.Right, visitor);
                    break;
            }
        }

        private static void VisitCalls(CallExpression call, Action<CallExpression> visitor)
        {
            visitor(call);
            foreach (Expression argument in call.Arguments)
                VisitCalls(argument, visitor);
        }
    }

    private enum ScriptType
    {
        Unknown,
        Void,
        Bool,
        Number,
        Duration,
        String
    }

    private sealed class BytecodeBuilder(Compilation owner)
    {
        private readonly List<ScriptInstruction> instructions = [];
        public int Count => instructions.Count;

        public void Emit(ScriptOpCode opcode, object? operand, SourceSpan span)
        {
            instructions.Add(new ScriptInstruction(opcode, operand, span));
            owner.CountInstruction();
        }

        public int EmitJump(ScriptOpCode opcode, SourceSpan span)
        {
            int index = Count;
            Emit(opcode, -1, span);
            return index;
        }

        public void PatchJump(int index, int target) => instructions[index] = instructions[index] with { Operand = target };

        public IReadOnlyList<ScriptInstruction> ToReadOnly() => instructions.AsReadOnly();
    }
}
