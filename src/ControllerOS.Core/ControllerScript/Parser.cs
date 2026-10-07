using ControllerOS.Core.Controls;

namespace ControllerOS.Core.ControllerScript;

internal sealed class Parser(IReadOnlyList<Token> tokens)
{
    private int position;
    private int expressionDepth;

    public ScriptProgram Parse()
    {
        var states = new List<StateDeclaration>();
        var functions = new List<FunctionDefinition>();
        var handlers = new List<EventHandlerDefinition>();
        SkipNewlines();

        while (Current.Kind != TokenKind.End)
        {
            if (IsWord("state"))
                states.Add(ParseState());
            else if (IsWord("def"))
                functions.Add(ParseFunction());
            else if (IsWord("on"))
                handlers.Add(ParseHandler());
            else
                Fail(Current, "Expected 'state', 'def', or 'on'.");
            SkipNewlines();
        }

        return new ScriptProgram(states.AsReadOnly(), functions.AsReadOnly(), handlers.AsReadOnly());
    }

    private StateDeclaration ParseState()
    {
        Token start = Advance();
        Token name = Expect(TokenKind.Identifier, "Expected a state variable name.");
        Expect(TokenKind.Equal, "Expected '=' after the state variable name.");
        Expression initialValue = ParseExpression();
        EndLine();
        return new StateDeclaration(name.Text, initialValue, start.Span);
    }

    private FunctionDefinition ParseFunction()
    {
        Token start = Advance();
        Token name = Expect(TokenKind.Identifier, "Expected a function name.");
        Expect(TokenKind.LeftParen, "Expected '(' after function name.");
        var parameters = new List<string>();
        if (Current.Kind != TokenKind.RightParen)
        {
            do
            {
                parameters.Add(Expect(TokenKind.Identifier, "Expected a parameter name.").Text);
            }
            while (Match(TokenKind.Comma));
        }
        Expect(TokenKind.RightParen, "Expected ')' after parameters.");
        Expect(TokenKind.Colon, "Expected ':' after function signature.");
        return new FunctionDefinition(name.Text, parameters.AsReadOnly(), ParseSuite(), start.Span);
    }

    private EventHandlerDefinition ParseHandler()
    {
        Token start = Advance();
        Token eventToken = Expect(TokenKind.Identifier, "Expected press, release, or change after 'on'.");
        ScriptEventKind kind = eventToken.Text switch
        {
            "press" => ScriptEventKind.Press,
            "release" => ScriptEventKind.Release,
            "change" => ScriptEventKind.Change,
            _ => throw Error(eventToken, "Expected press, release, or change after 'on'.")
        };
        Expect(TokenKind.LeftParen, "Expected '(' after event type.");
        Token controlToken = Expect(TokenKind.Identifier, "Expected a control name.");
        if (!ControlCatalog.TryParse(controlToken.Text, out ControlId control))
            Fail(controlToken, $"Unknown controller control '{controlToken.Text}'.");
        Expect(TokenKind.RightParen, "Expected ')' after control name.");
        Expect(TokenKind.Colon, "Expected ':' after event declaration.");
        return new EventHandlerDefinition(kind, control, ParseSuite(), start.Span);
    }

    private IReadOnlyList<Statement> ParseSuite()
    {
        EndLine();
        Expect(TokenKind.Indent, "Expected an indented block.");
        var statements = new List<Statement>();
        SkipNewlines();
        while (Current.Kind != TokenKind.Dedent && Current.Kind != TokenKind.End)
        {
            statements.Add(ParseStatement());
            SkipNewlines();
        }
        Expect(TokenKind.Dedent, "Expected the block to end.");
        return statements.AsReadOnly();
    }

    private Statement ParseStatement()
    {
        if (IsWord("if"))
            return ParseIf();

        if (IsWord("return"))
        {
            Token start = Advance();
            Expression? value = Current.Kind == TokenKind.Newline ? null : ParseExpression();
            EndLine();
            return new ReturnStatement(value, start.Span);
        }

        if (IsWord("output"))
        {
            Token start = Advance();
            Expect(TokenKind.Dot, "Expected '.' after output.");
            Token controlToken = Expect(TokenKind.Identifier, "Expected an output control name.");
            if (!ControlCatalog.TryParse(controlToken.Text, out ControlId control))
                Fail(controlToken, $"Unknown output control '{controlToken.Text}'.");
            Expect(TokenKind.Equal, "Expected '=' after output control.");
            Expression value = ParseExpression();
            EndLine();
            return new OutputAssignment(control, value, start.Span);
        }

        if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Equal)
        {
            Token name = Advance();
            Advance();
            Expression value = ParseExpression();
            EndLine();
            return new StateAssignment(name.Text, value, name.Span);
        }

        Expression expression = ParseExpression();
        if (expression is not CallExpression)
            throw new ControllerScriptException([new ScriptDiagnostic(expression.Span, "Only function calls can be used as expression statements.")]);
        var call = (CallExpression)expression;
        EndLine();
        return new CallStatement(call, call.Span);
    }

    private IfStatement ParseIf()
    {
        Token start = Advance();
        Expression condition = ParseExpression();
        Expect(TokenKind.Colon, "Expected ':' after condition.");
        IReadOnlyList<Statement> thenBody = ParseSuite();
        IReadOnlyList<Statement> elseBody = [];
        if (IsWord("else"))
        {
            Advance();
            Expect(TokenKind.Colon, "Expected ':' after else.");
            elseBody = ParseSuite();
        }
        return new IfStatement(condition, thenBody, elseBody, start.Span);
    }

    private Expression ParseExpression(int minimumPrecedence = 0)
    {
        if (expressionDepth >= 128)
            Fail(Current, "Expression nesting exceeds the 128-level limit.");
        expressionDepth++;
        try
        {
            return ParseExpressionCore(minimumPrecedence);
        }
        finally
        {
            expressionDepth--;
        }
    }

    private Expression ParseExpressionCore(int minimumPrecedence)
    {
        Expression left = ParsePrefix();
        while (true)
        {
            if (Current.Kind == TokenKind.LeftParen && left is NameExpression functionName)
            {
                if (9 < minimumPrecedence)
                    break;
                Token start = Current;
                Advance();
                var arguments = new List<Expression>();
                if (Current.Kind != TokenKind.RightParen)
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    }
                    while (Match(TokenKind.Comma));
                }
                Expect(TokenKind.RightParen, "Expected ')' after arguments.");
                left = new CallExpression(functionName.Name, arguments.AsReadOnly(), functionName.Span == default ? start.Span : functionName.Span);
                continue;
            }

            (int precedence, string op, bool rightAssociative) = BinaryOperator(Current);
            if (precedence < minimumPrecedence)
                break;
            Token operatorToken = Advance();
            int nextMinimum = rightAssociative ? precedence : precedence + 1;
            Expression right = ParseExpression(nextMinimum);
            left = new BinaryExpression(op, left, right, operatorToken.Span);
        }
        return left;
    }

    private Expression ParsePrefix()
    {
        Token token = Advance();
        switch (token.Kind)
        {
            case TokenKind.Number:
            case TokenKind.Duration:
            case TokenKind.String:
                return new LiteralExpression(token.Value!, token.Span);
            case TokenKind.Identifier when token.Text == "true":
                return new LiteralExpression(true, token.Span);
            case TokenKind.Identifier when token.Text == "false":
                return new LiteralExpression(false, token.Span);
            case TokenKind.Identifier when token.Text == "not":
                return new UnaryExpression("not", ParseExpression(3), token.Span);
            case TokenKind.Identifier:
                return new NameExpression(token.Text, token.Span);
            case TokenKind.Plus:
                return new UnaryExpression("+", ParseExpression(6), token.Span);
            case TokenKind.Minus:
                return new UnaryExpression("-", ParseExpression(6), token.Span);
            case TokenKind.LeftParen:
                Expression expression = ParseExpression();
                Expect(TokenKind.RightParen, "Expected ')' after expression.");
                return expression;
            default:
                Fail(token, "Expected an expression.");
                return null!;
        }
    }

    private static (int Precedence, string Operator, bool RightAssociative) BinaryOperator(Token token)
    {
        if (token.Kind == TokenKind.Identifier && token.Text is "and" or "or")
            return token.Text == "or" ? (1, token.Text, false) : (2, token.Text, false);

        return token.Kind switch
        {
            TokenKind.EqualEqual => (3, "==", false),
            TokenKind.BangEqual => (3, "!=", false),
            TokenKind.Less => (3, "<", false),
            TokenKind.LessEqual => (3, "<=", false),
            TokenKind.Greater => (3, ">", false),
            TokenKind.GreaterEqual => (3, ">=", false),
            TokenKind.Plus => (4, "+", false),
            TokenKind.Minus => (4, "-", false),
            TokenKind.Star => (5, "*", false),
            TokenKind.Slash => (5, "/", false),
            TokenKind.DoubleStar => (7, "**", true),
            _ => (-1, string.Empty, false)
        };
    }

    private bool IsWord(string word) => Current.Kind == TokenKind.Identifier && Current.Text == word;
    private Token Current => tokens[position];
    private Token Peek(int offset) => tokens[Math.Min(position + offset, tokens.Count - 1)];

    private Token Advance()
    {
        Token token = Current;
        if (token.Kind != TokenKind.End)
            position++;
        return token;
    }

    private bool Match(TokenKind kind)
    {
        if (Current.Kind != kind)
            return false;
        Advance();
        return true;
    }

    private Token Expect(TokenKind kind, string message)
    {
        if (Current.Kind != kind)
            Fail(Current, message);
        return Advance();
    }

    private void EndLine()
    {
        Expect(TokenKind.Newline, "Expected the end of the line.");
        SkipNewlines();
    }

    private void SkipNewlines()
    {
        while (Current.Kind == TokenKind.Newline)
            Advance();
    }

    private static void Fail(Token token, string message) => throw Error(token, message);
    private static ControllerScriptException Error(Token token, string message) =>
        new([new ScriptDiagnostic(token.Span, message)]);
}
