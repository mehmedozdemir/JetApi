using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JetApi.Testing.JsonPath;

/// <summary>A JSONPath syntax error at a position in the (trimmed) path.</summary>
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Never leaves the assembly: JsonPathQuery.Parse turns it into a result.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Only thrown by the parser with a position.")]
internal sealed class JsonPathSyntaxException(int position, string message) : Exception(message)
{
    public int Position { get; } = position;
}

/// <summary>
/// Recursive-descent parser for RFC 9535 JSONPath. The grammar follows the RFC; the only leniency is that whitespace
/// around the whole path is ignored.
/// </summary>
internal sealed class JsonPathParser(string path)
{
    private const long MaxIndex = 9007199254740991; // 2^53 - 1 (RFC 9535 I-JSON range)

    private readonly string _text = path.Trim();
    private int _pos;

    private bool AtEnd => _pos >= _text.Length;

    private char Peek => AtEnd ? '\0' : _text[_pos];

    public JsonPathQuery ParseQuery()
    {
        if (Peek != '$')
        {
            throw Error("Path_StartWithRoot");
        }

        _pos++;
        var segments = ParseSegments();
        SkipSpace();
        if (!AtEnd)
        {
            throw Unexpected();
        }

        return new JsonPathQuery(_text, segments);
    }

    private List<Segment> ParseSegments()
    {
        var segments = new List<Segment>();
        while (true)
        {
            var before = _pos;
            SkipSpace();
            if (Peek == '[')
            {
                segments.Add(new Segment(false, ParseBracketed()));
            }
            else if (StartsWith(".."))
            {
                _pos += 2;
                segments.Add(new Segment(true, Peek switch
                {
                    '[' => ParseBracketed(),
                    '*' => [Wildcard()],
                    _ => [new NameSelector(ParseShorthandName())],
                }));
            }
            else if (Peek == '.')
            {
                _pos++;
                segments.Add(new Segment(false, [Peek == '*' ? Wildcard() : new NameSelector(ParseShorthandName())]));
            }
            else
            {
                // Whitespace that does not lead to a segment belongs to whatever follows (e.g. "@.a == 1").
                _pos = before;
                return segments;
            }
        }
    }

    private WildcardSelector Wildcard()
    {
        _pos++;
        return new WildcardSelector();
    }

    private List<Selector> ParseBracketed()
    {
        Expect('[');
        SkipSpace();
        var selectors = new List<Selector> { ParseSelector() };
        while (true)
        {
            SkipSpace();
            if (Peek != ',')
            {
                break;
            }

            _pos++;
            SkipSpace();
            selectors.Add(ParseSelector());
        }

        Expect(']');
        return selectors;
    }

    private Selector ParseSelector()
    {
        switch (Peek)
        {
            case '\'' or '"':
                return new NameSelector(ParseString());
            case '*':
                return Wildcard();
            case '?':
                _pos++;
                SkipSpace();
                return new FilterSelector(ParseOr());
        }

        var start = ParseInteger();
        SkipSpace();
        if (Peek != ':')
        {
            return start is { } index ? new IndexSelector(index) : throw Unexpected();
        }

        _pos++;
        SkipSpace();
        var end = ParseInteger();
        SkipSpace();
        long? step = null;
        if (Peek == ':')
        {
            _pos++;
            SkipSpace();
            step = ParseInteger();
        }

        return new SliceSelector(start, end, step);
    }

    // int = "0" / ["-"] DIGIT1 *DIGIT
    private long? ParseInteger()
    {
        var begin = _pos;
        if (Peek == '-')
        {
            _pos++;
        }

        if (!char.IsAsciiDigit(Peek))
        {
            _pos = begin;
            return null;
        }

        var digitsAt = _pos;
        while (char.IsAsciiDigit(Peek))
        {
            _pos++;
        }

        var text = _text[begin.._pos];
        var digits = _text[digitsAt.._pos];
        if ((digits.Length > 1 && digits[0] == '0') || text == "-0"
            || !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || Math.Abs(value) > MaxIndex)
        {
            throw Error("Path_BadNumber", begin);
        }

        return value;
    }

    private string ParseShorthandName()
    {
        var begin = _pos;
        if (!IsNameFirst(Peek))
        {
            throw AtEnd ? Error("Path_UnexpectedEnd") : Error("Path_ExpectedName");
        }

        while (IsNameFirst(Peek) || char.IsAsciiDigit(Peek))
        {
            _pos++;
        }

        return _text[begin.._pos];

        static bool IsNameFirst(char c) => char.IsAsciiLetter(c) || c == '_' || c >= 0x80;
    }

    private string ParseString()
    {
        var quote = _text[_pos];
        var begin = _pos;
        _pos++;
        var builder = new StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                throw Error("Path_UnexpectedEnd");
            }

            var c = _text[_pos++];
            if (c == quote)
            {
                return builder.ToString();
            }

            if (c < 0x20)
            {
                throw Error("Path_BadString", _pos - 1);
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            var escaped = AtEnd ? throw Error("Path_UnexpectedEnd") : _text[_pos++];
            switch (escaped)
            {
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case '/': builder.Append('/'); break;
                case '\\': builder.Append('\\'); break;
                case 'u' when _pos + 4 <= _text.Length && int.TryParse(_text.AsSpan(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code):
                    builder.Append((char)code);
                    _pos += 4;
                    break;
                default:
                    if (escaped == quote)
                    {
                        builder.Append(quote);
                        break;
                    }

                    throw Error("Path_BadString", begin);
            }
        }
    }

    // ---- Filters ------------------------------------------------------------------------

    private FilterExpression ParseOr()
    {
        var parts = new List<FilterExpression> { ParseAnd() };
        while (TrySkip("||"))
        {
            parts.Add(ParseAnd());
        }

        return parts.Count == 1 ? parts[0] : new OrExpression(parts);
    }

    private FilterExpression ParseAnd()
    {
        var parts = new List<FilterExpression> { ParseBasic() };
        while (TrySkip("&&"))
        {
            parts.Add(ParseBasic());
        }

        return parts.Count == 1 ? parts[0] : new AndExpression(parts);
    }

    private FilterExpression ParseBasic()
    {
        SkipSpace();
        if (Peek == '!')
        {
            _pos++;
            SkipSpace();
            return new NotExpression(Peek == '(' ? ParseParenthesized() : ParseTest());
        }

        if (Peek == '(')
        {
            return ParseParenthesized();
        }

        var opAt = _pos;
        var left = ParseOperand();
        var before = _pos;
        SkipSpace();
        if (ReadComparisonOperator() is { } op)
        {
            SkipSpace();
            return new ComparisonExpression(left, op, ParseOperand());
        }

        _pos = before;
        return left is LiteralOperand ? throw Error("Path_NotATest", opAt) : new TestExpression(left);
    }

    private FilterExpression ParseParenthesized()
    {
        Expect('(');
        var inner = ParseOr();
        SkipSpace();
        Expect(')');
        return inner;
    }

    private TestExpression ParseTest()
    {
        var at = _pos;
        var operand = ParseOperand();
        return operand is LiteralOperand ? throw Error("Path_NotATest", at) : new TestExpression(operand);
    }

    private string? ReadComparisonOperator()
    {
        foreach (var op in (string[])["==", "!=", "<=", ">=", "<", ">"])
        {
            if (StartsWith(op))
            {
                _pos += op.Length;
                return op;
            }
        }

        return null;
    }

    private Operand ParseOperand()
    {
        switch (Peek)
        {
            case '@':
                _pos++;
                return new QueryOperand(true, ParseSegments());
            case '$':
                _pos++;
                return new QueryOperand(false, ParseSegments());
            case '\'' or '"':
                return new LiteralOperand(JsonValues.String(ParseString()));
            case '-':
            case >= '0' and <= '9':
                return new LiteralOperand(ParseNumber());
        }

        foreach (var (word, json) in (ReadOnlySpan<(string, string)>)[("true", "true"), ("false", "false"), ("null", "null")])
        {
            if (StartsWith(word) && !char.IsAsciiLetterOrDigit(_pos + word.Length < _text.Length ? _text[_pos + word.Length] : ' '))
            {
                _pos += word.Length;
                return new LiteralOperand(JsonDocument.Parse(json).RootElement.Clone());
            }
        }

        if (char.IsAsciiLetterLower(Peek))
        {
            return ParseFunction();
        }

        throw AtEnd ? Error("Path_UnexpectedEnd") : Unexpected();
    }

    private JsonElement ParseNumber()
    {
        var begin = _pos;
        if (Peek == '-')
        {
            _pos++;
        }

        SkipDigits();
        if (Peek == '.')
        {
            _pos++;
            SkipDigits();
        }

        if (Peek is 'e' or 'E')
        {
            _pos++;
            if (Peek is '+' or '-')
            {
                _pos++;
            }

            SkipDigits();
        }

        try
        {
            return JsonDocument.Parse(_text[begin.._pos]).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Error("Path_BadNumber", begin);
        }

        void SkipDigits()
        {
            while (char.IsAsciiDigit(Peek))
            {
                _pos++;
            }
        }
    }

    private FunctionOperand ParseFunction()
    {
        var begin = _pos;
        while (char.IsAsciiLetterLower(Peek) || char.IsAsciiDigit(Peek) || Peek == '_')
        {
            _pos++;
        }

        var name = _text[begin.._pos];
        if (!FunctionOperand.Names.Contains(name, StringComparer.Ordinal))
        {
            throw new JsonPathSyntaxException(begin, Messages.Format("Path_UnknownFunction", name));
        }

        Expect('(');
        var arguments = new List<Operand>();
        for (var i = 0; i < FunctionOperand.Arity(name); i++)
        {
            SkipSpace();
            if (i > 0)
            {
                Expect(',');
                SkipSpace();
            }

            arguments.Add(ParseOperand());
        }

        SkipSpace();
        Expect(')');
        return new FunctionOperand(name, arguments);
    }

    // ---- Helpers ------------------------------------------------------------------------

    private void SkipSpace()
    {
        while (Peek is ' ' or '\t' or '\n' or '\r')
        {
            _pos++;
        }
    }

    private bool TrySkip(string token)
    {
        var before = _pos;
        SkipSpace();
        if (StartsWith(token))
        {
            _pos += token.Length;
            return true;
        }

        _pos = before;
        return false;
    }

    private bool StartsWith(string token) => _text.AsSpan(_pos).StartsWith(token, StringComparison.Ordinal);

    private void Expect(char c)
    {
        if (Peek != c)
        {
            throw AtEnd
                ? new JsonPathSyntaxException(_pos, Messages.Format("Path_Expected", c))
                : new JsonPathSyntaxException(_pos, Messages.Format("Path_ExpectedInstead", c, Peek));
        }

        _pos++;
    }

    private JsonPathSyntaxException Unexpected() => AtEnd ? Error("Path_UnexpectedEnd") : new(_pos, Messages.Format("Path_Unexpected", Peek));

    private JsonPathSyntaxException Error(string key, int? at = null) => new(at ?? _pos, Messages.Get(key));
}
