using System.Text;

namespace JetApi.Import;

/// <summary>The shell a pasted command was written for.</summary>
internal enum ShellDialect
{
    /// <summary>bash/sh/zsh: Chrome and Firefox "Copy as cURL (bash)", Postman's export, most documentation.</summary>
    Bash,

    /// <summary>Windows cmd: Chrome's "Copy as cURL (cmd)" with <c>^</c> escapes.</summary>
    Cmd,

    /// <summary>PowerShell: backtick escapes and line continuations.</summary>
    PowerShell,
}

/// <summary>The words of a command line, after the shell's quoting rules were applied.</summary>
/// <param name="Words">The arguments, the program name first.</param>
/// <param name="IsUnterminated">A quote was not closed.</param>
/// <param name="ShellVariables">Shell variables that were turned into <c>{{name}}</c> placeholders, in order of appearance.</param>
internal sealed record TokenizedCommand(IReadOnlyList<string> Words, bool IsUnterminated, IReadOnlyList<string> ShellVariables);

/// <summary>
/// Splits a command line into words the way bash, cmd or PowerShell would, without running anything.
/// Shell variables (<c>$TOKEN</c>, <c>${TOKEN}</c>, <c>$env:TOKEN</c>) become JetApi placeholders (<c>{{TOKEN}}</c>).
/// The command ends at the first unquoted pipe, separator or redirection.
/// </summary>
internal static class CurlTokenizer
{
    public static ShellDialect Detect(string text)
    {
        if (text.Contains("^\n", StringComparison.Ordinal) || text.Contains("^\r\n", StringComparison.Ordinal) || text.Contains("^\"", StringComparison.Ordinal))
        {
            return ShellDialect.Cmd;
        }

        // Backtick continuations and escapes, $env: variables and the "&" call operator only make sense in PowerShell.
        var isPowerShell = text.Contains("`\n", StringComparison.Ordinal) || text.Contains("`\r\n", StringComparison.Ordinal)
            || text.Contains("`\"", StringComparison.Ordinal) || text.Contains("`$", StringComparison.Ordinal)
            || text.Contains("$env:", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('&');
        return isPowerShell ? ShellDialect.PowerShell : ShellDialect.Bash;
    }

    public static TokenizedCommand Tokenize(string text) => Detect(text) switch
    {
        ShellDialect.Cmd => new CmdReader(text).Read(),
        ShellDialect.PowerShell => new PowerShellReader(text).Read(),
        _ => new BashReader(text).Read(),
    };

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>Shared state: the words so far and the word being built.</summary>
    private abstract class Reader(string text)
    {
        private readonly List<string> _words = [];
        private readonly List<string> _variables = [];
        private readonly StringBuilder _word = new();
        private bool _inWord;

        protected string Text { get; } = text;

        protected int Position { get; set; }

        protected bool IsUnterminated { get; set; }

        protected bool AtEnd => Position >= Text.Length;

        protected char Current => Text[Position];

        public TokenizedCommand Read()
        {
            Scan();
            EndWord();
            return new TokenizedCommand(_words, IsUnterminated, _variables);
        }

        protected abstract void Scan();

        protected char Peek(int offset = 1) => Position + offset < Text.Length ? Text[Position + offset] : '\0';

        protected void Append(char c)
        {
            _word.Append(c);
            _inWord = true;
        }

        protected void Append(string value)
        {
            _word.Append(value);
            _inWord = true;
        }

        // An empty quoted string ('' or "") is still a word.
        protected void MarkWord() => _inWord = true;

        protected void EndWord()
        {
            if (_inWord)
            {
                _words.Add(_word.ToString());
            }

            _word.Clear();
            _inWord = false;
        }

        protected bool InWord => _inWord;

        protected void AppendVariable(string name)
        {
            Append("{{" + name + "}}");
            if (!_variables.Contains(name))
            {
                _variables.Add(name);
            }
        }

        // Skips "\n" or "\r\n" at the position and returns true when there was one.
        protected bool SkipNewline(int at)
        {
            if (at < Text.Length && Text[at] == '\n')
            {
                Position = at + 1;
                return true;
            }

            if (at + 1 < Text.Length && Text[at] == '\r' && Text[at + 1] == '\n')
            {
                Position = at + 2;
                return true;
            }

            return false;
        }

        protected static bool IsBlank(char c) => c is ' ' or '\t' or '\r' or '\n';

        // Pipes, command separators and redirections end the curl command (e.g. "curl ... | jq .").
        protected static bool IsCommandEnd(char c) => c is '|' or ';' or '&' or '>' or '<';

        protected void SkipComment()
        {
            while (!AtEnd && Current != '\n')
            {
                Position++;
            }
        }
    }

    private sealed class BashReader(string text) : Reader(text)
    {
        protected override void Scan()
        {
            while (!AtEnd)
            {
                var c = Current;
                if (IsBlank(c))
                {
                    EndWord();
                    Position++;
                }
                else if (c == '\\')
                {
                    if (!SkipNewline(Position + 1))
                    {
                        if (Position + 1 < Text.Length)
                        {
                            Append(Text[Position + 1]);
                        }

                        Position += 2;
                    }
                }
                else if (c == '\'')
                {
                    ReadSingleQuoted();
                }
                else if (c == '"')
                {
                    ReadDoubleQuoted();
                }
                else if (c == '$' && Peek() == '\'')
                {
                    Position++;
                    ReadAnsiC();
                }
                else if (c == '$' && Peek() == '"')
                {
                    Position++;
                    ReadDoubleQuoted();
                }
                else if (c == '$' && TryReadVariable())
                {
                    continue;
                }
                else if (c == '#' && !InWord)
                {
                    SkipComment();
                }
                else if (IsCommandEnd(c))
                {
                    return;
                }
                else
                {
                    Append(c);
                    Position++;
                }
            }
        }

        private void ReadSingleQuoted()
        {
            MarkWord();
            var end = Text.IndexOf('\'', Position + 1);
            if (end < 0)
            {
                IsUnterminated = true;
                Append(Text[(Position + 1)..]);
                Position = Text.Length;
                return;
            }

            Append(Text[(Position + 1)..end]);
            Position = end + 1;
        }

        private void ReadDoubleQuoted()
        {
            MarkWord();
            Position++;
            while (!AtEnd)
            {
                var c = Current;
                if (c == '"')
                {
                    Position++;
                    return;
                }

                if (c == '\\')
                {
                    var next = Peek();
                    if (SkipNewline(Position + 1))
                    {
                        continue;
                    }

                    if (next is '$' or '`' or '"' or '\\')
                    {
                        Append(next);
                        Position += 2;
                        continue;
                    }

                    Append(c);
                    Position++;
                    continue;
                }

                if (c == '$' && TryReadVariable())
                {
                    continue;
                }

                Append(c);
                Position++;
            }

            IsUnterminated = true;
        }

        // $NAME or ${NAME}; anything else (e.g. "$1", "$(...)", a lone "$") stays literal.
        private bool TryReadVariable()
        {
            var next = Peek();
            if (next == '{')
            {
                var end = Text.IndexOf('}', Position + 2);
                if (end > Position + 2 && IsNameStart(Text[Position + 2]) && Text[(Position + 2)..end].All(IsNameChar))
                {
                    AppendVariable(Text[(Position + 2)..end]);
                    Position = end + 1;
                    return true;
                }

                return false;
            }

            if (!IsNameStart(next))
            {
                return false;
            }

            var start = Position + 1;
            var stop = start;
            while (stop < Text.Length && IsNameChar(Text[stop]))
            {
                stop++;
            }

            AppendVariable(Text[start..stop]);
            Position = stop;
            return true;
        }

        // $'...' strings: C escapes, used by Chrome for values with quotes or newlines.
        private void ReadAnsiC()
        {
            MarkWord();
            Position++;
            while (!AtEnd)
            {
                var c = Current;
                if (c == '\'')
                {
                    Position++;
                    return;
                }

                if (c != '\\' || Position + 1 >= Text.Length)
                {
                    Append(c);
                    Position++;
                    continue;
                }

                var escape = Text[Position + 1];
                Position += 2;
                switch (escape)
                {
                    case 'a': Append('\a'); break;
                    case 'b': Append('\b'); break;
                    case 'e' or 'E': Append('\u001b'); break;
                    case 'f': Append('\f'); break;
                    case 'n': Append('\n'); break;
                    case 'r': Append('\r'); break;
                    case 't': Append('\t'); break;
                    case 'v': Append('\v'); break;
                    case 'x': AppendCode(ReadDigits(16, 2)); break;
                    case 'u': AppendCode(ReadDigits(16, 4)); break;
                    case 'U': AppendCode(ReadDigits(16, 8)); break;
                    case 'c' when !AtEnd:
                        Append((char)(Current & 0x1f));
                        Position++;
                        break;
                    case >= '0' and <= '7':
                        Position--;
                        AppendCode(ReadDigits(8, 3));
                        break;
                    default:
                        // \\ \' \" \? and unknown escapes keep the character.
                        Append(escape);
                        break;
                }
            }

            IsUnterminated = true;
        }

        private int? ReadDigits(int radix, int maxDigits)
        {
            var start = Position;
            while (Position < Text.Length && Position - start < maxDigits && IsDigit(Text[Position], radix))
            {
                Position++;
            }

            return Position == start ? null : Convert.ToInt32(Text[start..Position], radix);
        }

        private static bool IsDigit(char c, int radix) => radix == 8 ? c is >= '0' and <= '7' : char.IsAsciiHexDigit(c);

        private void AppendCode(int? code)
        {
            if (code is { } value && value <= 0x10FFFF)
            {
                Append(char.ConvertFromUtf32(value is >= 0xD800 and <= 0xDFFF ? 0xFFFD : value));
            }
        }
    }

    private sealed class PowerShellReader(string text) : Reader(text)
    {
        protected override void Scan()
        {
            // "& curl.exe ..." uses the call operator.
            SkipBlanks();
            if (!AtEnd && Current == '&' && IsBlank(Peek()))
            {
                Position++;
            }

            while (!AtEnd)
            {
                var c = Current;
                if (IsBlank(c))
                {
                    EndWord();
                    Position++;
                }
                else if (c == '`')
                {
                    if (!SkipNewline(Position + 1))
                    {
                        if (Position + 1 < Text.Length)
                        {
                            Append(Escape(Text[Position + 1]));
                        }

                        Position += 2;
                    }
                }
                else if (IsSingleQuote(c))
                {
                    ReadSingleQuoted();
                }
                else if (IsDoubleQuote(c))
                {
                    ReadDoubleQuoted();
                }
                else if (c == '$' && TryReadVariable())
                {
                    continue;
                }
                else if (c == '#' && !InWord)
                {
                    SkipComment();
                }
                else if (c is '|' or ';' or '>')
                {
                    return;
                }
                else
                {
                    Append(c);
                    Position++;
                }
            }
        }

        private void SkipBlanks()
        {
            while (!AtEnd && IsBlank(Current))
            {
                Position++;
            }
        }

        // PowerShell accepts typographic quotes as quotes.
        private static bool IsSingleQuote(char c) => c is '\'' or '‘' or '’' or '‚' or '‛';

        private static bool IsDoubleQuote(char c) => c is '"' or '“' or '”' or '„';

        private static char Escape(char c) => c switch
        {
            '0' => '\0',
            'a' => '\a',
            'b' => '\b',
            'e' => '\u001b',
            'f' => '\f',
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            'v' => '\v',
            _ => c,
        };

        private void ReadSingleQuoted()
        {
            MarkWord();
            Position++;
            while (!AtEnd)
            {
                if (IsSingleQuote(Current))
                {
                    if (IsSingleQuote(Peek()))
                    {
                        Append(Peek());
                        Position += 2;
                        continue;
                    }

                    Position++;
                    return;
                }

                Append(Current);
                Position++;
            }

            IsUnterminated = true;
        }

        private void ReadDoubleQuoted()
        {
            MarkWord();
            Position++;
            while (!AtEnd)
            {
                var c = Current;
                if (IsDoubleQuote(c))
                {
                    if (IsDoubleQuote(Peek()))
                    {
                        Append(Peek());
                        Position += 2;
                        continue;
                    }

                    Position++;
                    return;
                }

                if (c == '`' && Position + 1 < Text.Length)
                {
                    if (!SkipNewline(Position + 1))
                    {
                        Append(Escape(Text[Position + 1]));
                        Position += 2;
                    }

                    continue;
                }

                if (c == '$' && TryReadVariable())
                {
                    continue;
                }

                Append(c);
                Position++;
            }

            IsUnterminated = true;
        }

        // $NAME, ${NAME} and $env:NAME; "$(...)" and a lone "$" stay literal.
        private bool TryReadVariable()
        {
            var start = Position + 1;
            if (Peek() == '{')
            {
                var end = Text.IndexOf('}', Position + 2);
                if (end > Position + 2)
                {
                    var braced = Text[(Position + 2)..end];
                    braced = braced.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ? braced[4..] : braced;
                    if (braced.Length > 0 && IsNameStart(braced[0]) && braced.All(IsNameChar))
                    {
                        AppendVariable(braced);
                        Position = end + 1;
                        return true;
                    }
                }

                return false;
            }

            if (string.Compare(Text, start, "env:", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                start += 4;
            }

            if (start >= Text.Length || !IsNameStart(Text[start]))
            {
                return false;
            }

            var stop = start;
            while (stop < Text.Length && IsNameChar(Text[stop]))
            {
                stop++;
            }

            AppendVariable(Text[start..stop]);
            Position = stop;
            return true;
        }
    }

    /// <summary>
    /// cmd first removes its <c>^</c> escapes (outside its own quotes), then curl.exe splits the result with the
    /// Microsoft C runtime rules (double quotes group, <c>\"</c> is a literal quote).
    /// </summary>
    private sealed class CmdReader(string text) : Reader(Unescape(text))
    {
        protected override void Scan()
        {
            var quoted = false;
            while (!AtEnd)
            {
                var c = Current;
                if (c == '\\')
                {
                    var count = 0;
                    while (!AtEnd && Current == '\\')
                    {
                        count++;
                        Position++;
                    }

                    if (!AtEnd && Current == '"')
                    {
                        Append(new string('\\', count / 2));
                        if (count % 2 == 1)
                        {
                            Append('"');
                            Position++;
                        }
                    }
                    else
                    {
                        Append(new string('\\', count));
                    }
                }
                else if (c == '"')
                {
                    MarkWord();
                    if (quoted && Peek() == '"')
                    {
                        Append('"');
                        Position += 2;
                        continue;
                    }

                    quoted = !quoted;
                    Position++;
                }
                else if (!quoted && IsBlank(c))
                {
                    EndWord();
                    Position++;
                }
                else
                {
                    Append(c);
                    Position++;
                }
            }
        }

        // cmd's own pass: "^x" is a literal x, "^" + newline continues the line and escapes the next character
        // (so "^\n\n" is a literal newline, as Chrome writes it). Unquoted & | < > end the command.
        private static string Unescape(string text)
        {
            var result = new StringBuilder(text.Length);
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    quoted = !quoted;
                    result.Append(c);
                }
                else if (c == '^' && !quoted && i + 1 < text.Length)
                {
                    i++;
                    if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    if (text[i] == '\n')
                    {
                        // The character after the line break is escaped.
                        if (i + 1 >= text.Length)
                        {
                            break;
                        }

                        i++;
                        if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        {
                            i++;
                        }
                    }

                    result.Append(text[i]);
                }
                else if (!quoted && c is '&' or '|' or '<' or '>')
                {
                    break;
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }
    }
}
