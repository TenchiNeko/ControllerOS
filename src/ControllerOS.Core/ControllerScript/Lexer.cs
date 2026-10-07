using System.Globalization;

namespace ControllerOS.Core.ControllerScript;

internal enum TokenKind
{
    End,
    Newline,
    Indent,
    Dedent,
    Identifier,
    Number,
    Duration,
    String,
    LeftParen,
    RightParen,
    Colon,
    Dot,
    Comma,
    Equal,
    EqualEqual,
    BangEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Plus,
    Minus,
    Star,
    DoubleStar,
    Slash
}

internal readonly record struct Token(TokenKind Kind, string Text, object? Value, SourceSpan Span);

internal sealed class Lexer(string source)
{
    private readonly List<ScriptDiagnostic> diagnostics = [];
    private readonly List<Token> tokens = [];

    public IReadOnlyList<Token> Lex()
    {
        string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        SourceSpan endOfFile = normalized.EndsWith('\n')
            ? new SourceSpan(lines.Length, 1)
            : new SourceSpan(lines.Length, lines[^1].Length + 1);
        var indentation = new Stack<int>();
        indentation.Push(0);

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            int index = 0;
            while (index < line.Length && line[index] == ' ')
                index++;
            if (index < line.Length && line[index] == '\t')
            {
                AddError(lineIndex + 1, index + 1, "Tabs are not allowed for indentation; use four spaces.");
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
            }

            if (index == line.Length || line[index] == '#')
                continue;

            int previousIndent = indentation.Peek();
            if (index > previousIndent)
            {
                if (index != previousIndent + 4)
                    AddError(lineIndex + 1, 1, "A block must be indented by four spaces.");
                if (indentation.Count >= 65)
                    AddError(lineIndex + 1, 1, "Blocks may be nested at most 64 levels.");
                indentation.Push(index);
                tokens.Add(new Token(TokenKind.Indent, string.Empty, null, new SourceSpan(lineIndex + 1, 1)));
            }
            else if (index < previousIndent)
            {
                while (indentation.Count > 1 && index < indentation.Peek())
                {
                    indentation.Pop();
                    tokens.Add(new Token(TokenKind.Dedent, string.Empty, null, new SourceSpan(lineIndex + 1, index + 1)));
                }
                if (index != indentation.Peek())
                    AddError(lineIndex + 1, index + 1, "Indentation does not match an outer block.");
            }

            ScanLine(line, index, lineIndex + 1);
            tokens.Add(new Token(TokenKind.Newline, string.Empty, null, new SourceSpan(lineIndex + 1, line.Length + 1)));
        }

        while (indentation.Count > 1)
        {
            indentation.Pop();
            tokens.Add(new Token(TokenKind.Dedent, string.Empty, null, endOfFile));
        }
        tokens.Add(new Token(TokenKind.End, string.Empty, null, endOfFile));

        if (diagnostics.Count > 0)
            throw new ControllerScriptException(diagnostics.AsReadOnly());
        return tokens.AsReadOnly();
    }

    private void ScanLine(string line, int index, int lineNumber)
    {
        while (index < line.Length)
        {
            char c = line[index];
            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }
            if (c == '#')
                break;

            int start = index;
            SourceSpan span = new(lineNumber, start + 1);
            if (char.IsAsciiLetter(c) || c == '_')
            {
                index++;
                while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] == '_'))
                    index++;
                string word = line[start..index];
                Add(TokenKind.Identifier, word, null, span);
                continue;
            }
            if (char.IsAsciiDigit(c))
            {
                index++;
                while (index < line.Length && char.IsAsciiDigit(line[index]))
                    index++;
                if (index + 1 < line.Length && line[index] == '.' && char.IsAsciiDigit(line[index + 1]))
                {
                    index++;
                    while (index < line.Length && char.IsAsciiDigit(line[index]))
                        index++;
                }

                string number = line[start..index];
                if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double numeric) || !double.IsFinite(numeric))
                {
                    AddError(lineNumber, start + 1, "Invalid numeric literal.");
                    continue;
                }

                int suffixStart = index;
                while (index < line.Length && char.IsAsciiLetter(line[index]))
                    index++;
                string suffix = line[suffixStart..index];
                if (suffix.Length == 0)
                    Add(TokenKind.Number, number, numeric, span);
                else if (suffix is "ms" or "s")
                {
                    double maximum = suffix == "ms" ? 3_600_000.0 : 3_600.0;
                    if (numeric > maximum)
                        AddError(lineNumber, start + 1, "Duration must be between 0 and 1 hour.");
                    else
                    {
                        TimeSpan duration = suffix == "ms"
                            ? TimeSpan.FromMilliseconds(numeric)
                            : TimeSpan.FromSeconds(numeric);
                        Add(TokenKind.Duration, line[start..index], duration, span);
                    }
                }
                else
                    AddError(lineNumber, suffixStart + 1, $"Unknown numeric suffix '{suffix}'. Use ms or s for durations.");
                continue;
            }
            if (c is '\'' or '"')
            {
                ScanString(line, ref index, lineNumber, span, c);
                continue;
            }

            index++;
            switch (c)
            {
                case '(': Add(TokenKind.LeftParen, "(", null, span); break;
                case ')': Add(TokenKind.RightParen, ")", null, span); break;
                case ':': Add(TokenKind.Colon, ":", null, span); break;
                case '.': Add(TokenKind.Dot, ".", null, span); break;
                case ',': Add(TokenKind.Comma, ",", null, span); break;
                case '+': Add(TokenKind.Plus, "+", null, span); break;
                case '-': Add(TokenKind.Minus, "-", null, span); break;
                case '/': Add(TokenKind.Slash, "/", null, span); break;
                case '*':
                    if (index < line.Length && line[index] == '*')
                    {
                        index++;
                        Add(TokenKind.DoubleStar, "**", null, span);
                    }
                    else
                        Add(TokenKind.Star, "*", null, span);
                    break;
                case '=':
                    if (index < line.Length && line[index] == '=')
                    {
                        index++;
                        Add(TokenKind.EqualEqual, "==", null, span);
                    }
                    else
                        Add(TokenKind.Equal, "=", null, span);
                    break;
                case '!':
                    if (index < line.Length && line[index] == '=')
                    {
                        index++;
                        Add(TokenKind.BangEqual, "!=", null, span);
                    }
                    else
                        AddError(lineNumber, start + 1, "Expected '=' after '!'.");
                    break;
                case '<':
                    if (index < line.Length && line[index] == '=')
                    {
                        index++;
                        Add(TokenKind.LessEqual, "<=", null, span);
                    }
                    else
                        Add(TokenKind.Less, "<", null, span);
                    break;
                case '>':
                    if (index < line.Length && line[index] == '=')
                    {
                        index++;
                        Add(TokenKind.GreaterEqual, ">=", null, span);
                    }
                    else
                        Add(TokenKind.Greater, ">", null, span);
                    break;
                default: AddError(lineNumber, start + 1, $"Unexpected character '{c}'."); break;
            }
        }
    }

    private void ScanString(string line, ref int index, int lineNumber, SourceSpan span, char quote)
    {
        index++;
        var value = new System.Text.StringBuilder();
        while (index < line.Length && line[index] != quote)
        {
            char current = line[index++];
            if (current == '\\')
            {
                if (index == line.Length)
                    break;
                current = line[index++] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '\'' => '\'',
                    '"' => '"',
                    _ => '\0'
                };
                if (current == '\0')
                {
                    AddError(lineNumber, index, "Unsupported string escape.");
                    return;
                }
            }
            value.Append(current);
            if (value.Length > 256)
            {
                AddError(lineNumber, span.Column, "String literals are limited to 256 characters.");
                return;
            }
        }

        if (index == line.Length || line[index] != quote)
        {
            AddError(lineNumber, span.Column, "Unterminated string literal.");
            return;
        }
        index++;
        int start = span.Column - 1;
        Add(TokenKind.String, line[start..index], value.ToString(), span);
    }

    private void Add(TokenKind kind, string text, object? value, SourceSpan span) =>
        tokens.Add(new Token(kind, text, value, span));

    private void AddError(int line, int column, string message) =>
        diagnostics.Add(new ScriptDiagnostic(new SourceSpan(line, column), message));
}
