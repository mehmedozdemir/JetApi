using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JetApi.Core.Model;

namespace JetApi.Import;

/// <summary>What a Postman test script became.</summary>
/// <param name="Checks">Checks for the request's <c>tests</c>.</param>
/// <param name="Extractions">Rules for the request's <c>extract</c>.</param>
/// <param name="IsComplete">Every statement was understood, so the script itself need not be kept.</param>
internal sealed record ScriptConversion(IReadOnlyList<ResponseCheck> Checks, IReadOnlyList<Extraction> Extractions, bool IsComplete);

/// <summary>
/// Turns the common statements of Postman test scripts into declarative checks and extractions (ADR-0010):
/// <c>pm.response.to.have.status(200)</c>, <c>pm.expect(json.a.b).to.eql(…)</c>, response time, headers, body text,
/// <c>pm.environment.set("token", json.token)</c> and the older <c>tests[…]</c> / <c>postman.set…</c> forms. Statements
/// it does not recognise make the conversion incomplete; the caller then keeps the script in the file.
/// </summary>
internal static partial class PostmanScripts
{
    private const string Str = """(?:'(?:[^'\\\n]|\\.)*'|"(?:[^"\\\n]|\\.)*"|`(?:[^`\\$]|\\.)*`)""";
    private const string Literal = Str + """|-?\d+(?:\.\d+)?|true|false|null""";
    private const string Accessor = """(?:\??\.[A-Za-z_$][\w$]*|\[\s*\d+\s*\]|\[\s*""" + Str + """\s*\])*""";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    /// <summary>Converts a test script.</summary>
    public static ScriptConversion ConvertTest(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var text = new StringBuilder(StripComments(script));
        var checks = new List<ResponseCheck>();
        var extractions = new List<Extraction>();

        // var json = pm.response.json();  (also JSON.parse(responseBody))
        var aliases = new List<string>();
        Consume(text, JsonAlias(), m =>
        {
            aliases.Add(Regex.Escape(m.Groups["name"].Value));
            return true;
        });
        var json = $"""(?:pm\.response\.json\(\)|JSON\.parse\(\s*responseBody\s*\){(aliases.Count == 0 ? string.Empty : "|" + string.Join('|', aliases.Select(a => a + @"\b")))})""";
        var jsonValue = $"(?<json>{json})(?<path>{Accessor})";
        var not = @"(?<not>\.not)?";

        // Status.
        Consume(text, Rule(@"pm\.response\.to\.have\.status\(\s*(?<code>\d{3})\s*\)"), m => Add(checks, Status(CheckOperator.Equals, m.Groups["code"].Value)));
        Consume(text, Rule(@"pm\.response\.to\.be\.ok\b"), _ => Add(checks, Status(CheckOperator.Equals, "200")));
        Consume(text, Rule(@"pm\.response\.to\.be\.success\b"), _ => Add(checks, Status(CheckOperator.GreaterThan, "199"), Status(CheckOperator.LessThan, "300")));
        Consume(text, Rule($@"pm\.expect\(\s*pm\.response\.code\s*\)\.to{not}\.(?:eql|equal|equals|eq)\(\s*(?<code>\d{{3}})\s*\)"),
            m => Add(checks, Status(m.Groups["not"].Success ? CheckOperator.NotEquals : CheckOperator.Equals, m.Groups["code"].Value)));
        Consume(text, Rule($@"tests\[\s*{Str}\s*\]\s*=\s*responseCode\.code\s*===?\s*(?<code>\d{{3}})"), m => Add(checks, Status(CheckOperator.Equals, m.Groups["code"].Value)));

        // Response time.
        Consume(text, Rule(@"pm\.expect\(\s*pm\.response\.responseTime\s*\)\.to\.be\.(?<op>below|lessThan|lt|above|greaterThan|gt)\(\s*(?<ms>\d+)\s*\)"),
            m => Add(checks, new ResponseCheck(CheckSource.Time, m.Groups["op"].Value is "below" or "lessThan" or "lt" ? CheckOperator.LessThan : CheckOperator.GreaterThan, Expected: m.Groups["ms"].Value)));
        Consume(text, Rule($@"tests\[\s*{Str}\s*\]\s*=\s*responseTime\s*<\s*(?<ms>\d+)"), m => Add(checks, new ResponseCheck(CheckSource.Time, CheckOperator.LessThan, Expected: m.Groups["ms"].Value)));

        // Headers.
        Consume(text, Rule($@"pm\.response\.to(?<not>\.not)?\.(?:have|be)\.header\(\s*(?<name>{Str})\s*(?:,\s*(?<value>{Str})\s*)?\)"), m =>
        {
            var name = Unquote(m.Groups["name"].Value);
            return m.Groups["value"].Success
                ? !m.Groups["not"].Success && Add(checks, new ResponseCheck(CheckSource.Header, CheckOperator.Equals, name, Unquote(m.Groups["value"].Value)))
                : Add(checks, new ResponseCheck(CheckSource.Header, m.Groups["not"].Success ? CheckOperator.NotExists : CheckOperator.Exists, name));
        });
        Consume(text, Rule($@"pm\.expect\(\s*pm\.response\.headers\.get\(\s*(?<name>{Str})\s*\)\s*\)\.to{not}(?:\.be)?\.(?<op>\w+)(?:\(\s*(?<value>{Literal}|/(?:[^/\\\n]|\\.)+/[a-z]*)?\s*\))?"),
            m => Operator(m) is { } op && Add(checks, new ResponseCheck(CheckSource.Header, op, Unquote(m.Groups["name"].Value), Value(m))));

        // Body text.
        Consume(text, Rule($@"pm\.response\.to\.have\.body\(\s*(?<value>{Str})\s*\)"), m => Add(checks, new ResponseCheck(CheckSource.Body, CheckOperator.Equals, Expected: Unquote(m.Groups["value"].Value))));
        Consume(text, Rule($@"pm\.expect\(\s*(?:pm\.response\.text\(\)|responseBody)\s*\)\.to{not}(?:\.be)?\.(?<op>\w+)(?:\(\s*(?<value>{Literal}|/(?:[^/\\\n]|\\.)+/[a-z]*)?\s*\))?"),
            m => Operator(m) is { } op && op is not (CheckOperator.GreaterThan or CheckOperator.LessThan) && Add(checks, new ResponseCheck(CheckSource.Body, op, Expected: Value(m))));
        Consume(text, Rule($@"tests\[\s*{Str}\s*\]\s*=\s*responseBody\.has\(\s*(?<value>{Str})\s*\)"), m => Add(checks, new ResponseCheck(CheckSource.Body, CheckOperator.Contains, Expected: Unquote(m.Groups["value"].Value))));

        // JSON values.
        Consume(text, Rule($@"pm\.expect\(\s*{jsonValue}\s*\)\.to{not}\.have\.property\(\s*(?<prop>{Str})\s*(?:,\s*(?<value>{Literal})\s*)?\)"), m =>
        {
            if (JsonPath(m.Groups["path"].Value, Unquote(m.Groups["prop"].Value)) is not { } path)
            {
                return false;
            }

            return m.Groups["value"].Success
                ? Add(checks, new ResponseCheck(CheckSource.Json, m.Groups["not"].Success ? CheckOperator.NotEquals : CheckOperator.Equals, path, Expected(m.Groups["value"].Value)))
                : Add(checks, new ResponseCheck(CheckSource.Json, m.Groups["not"].Success ? CheckOperator.NotExists : CheckOperator.Exists, path));
        });
        Consume(text, Rule($@"pm\.expect\(\s*{jsonValue}\s*\)\.to{not}\.be\.(?<value>true|false|null)\b"),
            m => JsonPath(m.Groups["path"].Value) is { } path && Add(checks, new ResponseCheck(CheckSource.Json, m.Groups["not"].Success ? CheckOperator.NotEquals : CheckOperator.Equals, path, m.Groups["value"].Value)));
        Consume(text, Rule($@"pm\.expect\(\s*{jsonValue}\s*\)\.to{not}(?:\.be)?\.(?<op>\w+)(?:\(\s*(?<value>{Literal}|/(?:[^/\\\n]|\\.)+/[a-z]*)?\s*\))?"),
            m => JsonPath(m.Groups["path"].Value) is { } path && Operator(m) is { } op && Add(checks, new ResponseCheck(CheckSource.Json, op, path, Value(m, json: true))));

        // Extractions: pm.environment.set("token", json.token) and the older postman.setEnvironmentVariable.
        var setter = @"(?:pm\.(?<scope>environment|collectionVariables|globals|variables)\.set|postman\.set(?<old>Environment|Global)Variable)";
        Consume(text, Rule($@"{setter}\(\s*(?<name>{Str})\s*,\s*(?:{jsonValue}|pm\.response\.headers\.get\(\s*(?<header>{Str})\s*\)|postman\.getResponseHeader\(\s*(?<header>{Str})\s*\)|(?<status>pm\.response\.code|responseCode\.code)|(?<body>pm\.response\.text\(\)|responseBody))\s*\)"), m =>
        {
            var variable = Unquote(m.Groups["name"].Value);
            var target = (m.Groups["scope"].Value, m.Groups["old"].Value) switch
            {
                ("environment", _) or (_, "Environment") => ExtractionTarget.Environment,
                ("collectionVariables", _) => ExtractionTarget.Collection,
                ("globals", _) or (_, "Global") => ExtractionTarget.Global,
                _ => ExtractionTarget.Runtime,
            };
            if (m.Groups["json"].Success)
            {
                return JsonPath(m.Groups["path"].Value) is { } path && Add(extractions, new Extraction(ExtractionSource.Json, path, variable, target));
            }

            return m.Groups["header"].Success ? Add(extractions, new Extraction(ExtractionSource.Header, Unquote(m.Groups["header"].Value), variable, target))
                : m.Groups["status"].Success ? Add(extractions, new Extraction(ExtractionSource.Status, string.Empty, variable, target))
                : Add(extractions, new Extraction(ExtractionSource.Body, string.Empty, variable, target));
        });

        // What is left must be only the pm.test wrappers and punctuation.
        Consume(text, Rule($@"pm\.test\(\s*{Str}\s*,\s*(?:function\s*\(\s*\)|\(\s*\)\s*=>|async\s*\(\s*\)\s*=>)"), _ => true);
        var rest = Leftover().Replace(text.ToString(), string.Empty);
        return new ScriptConversion(checks, extractions, rest.Length == 0);
    }

    private static bool Add<T>(List<T> list, params T[] items)
    {
        list.AddRange(items);
        return true;
    }

    private static ResponseCheck Status(CheckOperator op, string code) => new(CheckSource.Status, op, Expected: code);

    // Runs a rule over the text; each match the handler accepts is blanked so it is not seen again.
    private static void Consume(StringBuilder text, Regex rule, Func<Match, bool> handle)
    {
        foreach (var match in rule.Matches(text.ToString()).Reverse())
        {
            if (handle(match))
            {
                for (var i = match.Index; i < match.Index + match.Length; i++)
                {
                    text[i] = text[i] == '\n' ? '\n' : ' ';
                }
            }
        }
    }

    private static Regex Rule(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, Timeout);

    private static CheckOperator? Operator(Match match)
    {
        var negated = match.Groups["not"].Success;
        var hasValue = match.Groups["value"].Success;
        return (match.Groups["op"].Value, negated, hasValue) switch
        {
            ("eql" or "equal" or "equals" or "eq", false, true) => CheckOperator.Equals,
            ("eql" or "equal" or "equals" or "eq", true, true) => CheckOperator.NotEquals,
            ("include" or "includes" or "contain" or "contains", false, true) => CheckOperator.Contains,
            ("include" or "includes" or "contain" or "contains", true, true) => CheckOperator.NotContains,
            ("above" or "greaterThan" or "gt", false, true) => CheckOperator.GreaterThan,
            ("below" or "lessThan" or "lt", false, true) => CheckOperator.LessThan,
            ("match", false, true) when IsRegex(match.Groups["value"].Value) => CheckOperator.Matches,
            ("exist" or "ok", false, false) => CheckOperator.Exists,
            ("exist", true, false) => CheckOperator.NotExists,
            _ => null,
        };
    }

    private static bool IsRegex(string value) => value.StartsWith('/') && JsRegex(value) is not null;

    // The expected value as JetApi writes it: text unquoted, numbers as written, a /regex/ as a .NET pattern.
    private static string Value(Match match, bool json = false)
    {
        if (!match.Groups["value"].Success)
        {
            return string.Empty;
        }

        var value = match.Groups["value"].Value;
        return value.StartsWith('/') ? JsRegex(value)! : json ? Expected(value) : Unquote(value);
    }

    // A JSON check compares a text literal with string values; text that reads as another JSON value stays quoted.
    private static string Expected(string literal)
    {
        if (literal[0] is not ('\'' or '"' or '`'))
        {
            return literal;
        }

        var text = Unquote(literal);
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.String ? text : JsonSerializer.Serialize(text);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    // "/^ab+c$/i" -> "(?i)^ab+c$"; other flags have no equivalent here.
    private static string? JsRegex(string literal)
    {
        var end = literal.LastIndexOf('/');
        var flags = literal[(end + 1)..];
        var pattern = literal[1..end];
        return flags switch
        {
            "" => pattern,
            "i" => "(?i)" + pattern,
            _ => null,
        };
    }

    // ".data[0]['user-id']" -> "$.data[0]['user-id']"; ".length" and similar JavaScript members cannot be expressed.
    private static string? JsonPath(string accessor, string? property = null)
    {
        var builder = new StringBuilder("$");
        foreach (Match part in AccessorPart().Matches(accessor))
        {
            if (part.Groups["name"].Success)
            {
                if (part.Groups["name"].Value is "length")
                {
                    return null;
                }

                Member(part.Groups["name"].Value);
            }
            else if (part.Groups["index"].Success)
            {
                builder.Append('[').Append(part.Groups["index"].Value).Append(']');
            }
            else
            {
                Member(Unquote(part.Groups["key"].Value));
            }
        }

        if (property is not null)
        {
            Member(property);
        }

        return builder.ToString();

        void Member(string name)
        {
            if (SimpleName().IsMatch(name))
            {
                builder.Append('.').Append(name);
            }
            else
            {
                builder.Append("['").Append(name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)).Append("']");
            }
        }
    }

    // A JavaScript string literal's value.
    private static string Unquote(string literal)
    {
        var builder = new StringBuilder();
        for (var i = 1; i < literal.Length - 1; i++)
        {
            var c = literal[i];
            if (c != '\\' || i + 1 >= literal.Length - 1)
            {
                builder.Append(c);
                continue;
            }

            var next = literal[++i];
            switch (next)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'u' when i + 4 < literal.Length - 1 && int.TryParse(literal.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                    builder.Append((char)code);
                    i += 4;
                    break;
                default:
                    builder.Append(next);
                    break;
            }
        }

        return builder.ToString();
    }

    // Removes // and /* */ comments outside string literals (a regex literal with "//" is rare in tests).
    private static string StripComments(string script)
    {
        var builder = new StringBuilder(script.Length);
        char? quote = null;
        for (var i = 0; i < script.Length; i++)
        {
            var c = script[i];
            if (quote is not null)
            {
                builder.Append(c);
                if (c == '\\' && i + 1 < script.Length)
                {
                    builder.Append(script[++i]);
                }
                else if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
                builder.Append(c);
            }
            else if (c == '/' && i + 1 < script.Length && script[i + 1] == '/')
            {
                while (i < script.Length && script[i] != '\n')
                {
                    i++;
                }

                builder.Append('\n');
            }
            else if (c == '/' && i + 1 < script.Length && script[i + 1] == '*')
            {
                var end = script.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? script.Length : end + 1;
                builder.Append(' ');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex("""(?:var|let|const)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*(?:pm\.response\.json\(\)|JSON\.parse\(\s*responseBody\s*\))\s*;?""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex JsonAlias();

    [GeneratedRegex("""\??\.(?<name>[A-Za-z_$][\w$]*)|\[\s*(?<index>\d+)\s*\]|\[\s*(?<key>'(?:[^'\\\n]|\\.)*'|"(?:[^"\\\n]|\\.)*"|`(?:[^`\\$]|\\.)*`)\s*\]""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex AccessorPart();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleName();

    [GeneratedRegex("""[\s{}();,]""", RegexOptions.CultureInvariant)]
    private static partial Regex Leftover();
}
