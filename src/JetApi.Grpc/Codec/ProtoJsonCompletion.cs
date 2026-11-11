using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Codec;

/// <summary>An edit that inserts a field: replace <see cref="Length"/> characters at <see cref="Start"/> with <see cref="Text"/>.</summary>
/// <param name="Start">Where the replaced text starts.</param>
/// <param name="Length">How many characters to replace.</param>
/// <param name="Text">The text to insert.</param>
/// <param name="SelectionStart">Where the caret (or the selected placeholder value) starts after the edit.</param>
/// <param name="SelectionLength">How many characters to select there (0 for a plain caret).</param>
public sealed record ProtoJsonEdit(int Start, int Length, string Text, int SelectionStart, int SelectionLength);

/// <summary>
/// The fields that can be written where the caret is in a message's proto3 JSON, e.g. after <c>{</c>, a comma, or
/// in a property name being typed. Nested messages, repeated messages and map values are followed; fields already
/// in the object (and the other members of a oneof that is already set) are left out.
/// </summary>
public sealed class ProtoJsonCompletion
{
    private readonly string _text;
    private readonly int _start;
    private readonly int _end;
    private readonly bool _isKeyName;
    private readonly int? _commaAt;
    private readonly bool _commaAfter;

    private ProtoJsonCompletion(
        string text,
        ProtoMessageType type,
        string prefix,
        IReadOnlyList<ProtoField> fields,
        bool isTyped,
        (int Start, int End) replaced,
        bool isKeyName,
        int? commaAt,
        bool commaAfter)
    {
        _text = text;
        Type = type;
        Prefix = prefix;
        Fields = fields;
        IsTyped = isTyped;
        (_start, _end) = replaced;
        _isKeyName = isKeyName;
        _commaAt = commaAt;
        _commaAfter = commaAfter;
    }

    /// <summary>The message whose fields are offered.</summary>
    public ProtoMessageType Type { get; }

    /// <summary>The part of the name typed before the caret (without the quote).</summary>
    public string Prefix { get; }

    /// <summary>
    /// The fields that can be added, in declaration order: names that start with <see cref="Prefix"/> first, then
    /// names that contain it.
    /// </summary>
    public IReadOnlyList<ProtoField> Fields { get; }

    /// <summary>A property name has been started at the caret (a quote or letters), not just a free position.</summary>
    public bool IsTyped { get; }

    /// <summary>Where the name being typed starts (the caret when none is started).</summary>
    public int Start => _start;

    /// <summary>
    /// Finds what can be written at <paramref name="caret"/> in <paramref name="text"/>, the JSON of a
    /// <paramref name="root"/> message. Returns <c>null</c> when the caret is not where a property name goes
    /// (in a value, outside the object, in a map or <c>Struct</c>, or after invalid JSON).
    /// </summary>
    public static ProtoJsonCompletion? Find(ProtoMessageType root, string? text, int caret)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (text is null || caret < 0 || caret > text.Length)
        {
            return null;
        }

        var tokens = Tokenize(text);
        var stack = new Stack<Frame>();
        for (var i = 0; i <= tokens.Count; i++)
        {
            var token = i < tokens.Count ? tokens[i] : (Token?)null;
            if (token is null || token.Value.Start >= caret)
            {
                // The caret is between tokens.
                return stack.TryPeek(out var frame) ? Create(text, tokens, i, frame, (caret, caret), isTyped: false, prefix: string.Empty) : null;
            }

            var t = token.Value;
            var inside = t.Kind == TokenKind.String
                ? caret < t.End || (!t.Closed && caret == t.End)
                : caret <= t.End;
            if (inside && t.Kind is TokenKind.String or TokenKind.Literal)
            {
                if (!stack.TryPeek(out var frame))
                {
                    return null;
                }

                var nameStart = t.Kind == TokenKind.String ? t.Start + 1 : t.Start;
                return Create(text, tokens, i + 1, frame, (t.Start, t.End), isTyped: true, prefix: text[nameStart..caret]);
            }

            if (!Step(stack, t, i == 0 ? root : null, text))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The edit that writes <paramref name="field"/> with a placeholder value of its type.</summary>
    public ProtoJsonEdit Insert(ProtoField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var name = "\"" + field.JsonName + "\"";
        string text;
        int selectFrom;
        int selectLength;
        if (_isKeyName)
        {
            // Renaming an existing property: the value stays.
            text = name;
            selectFrom = text.Length;
            selectLength = 0;
        }
        else
        {
            var (value, offset, length) = PlaceholderValue(field);
            text = name + ": " + value;
            selectFrom = name.Length + 2 + offset;
            selectLength = length;
        }

        if (_commaAfter)
        {
            text += ",";
        }

        var start = _start;
        if (_commaAt is { } comma)
        {
            // A missing comma after the previous value: insert it there and keep the whitespace in between.
            var between = "," + _text[comma.._start];
            text = between + text;
            selectFrom += between.Length;
            start = comma;
        }

        return new ProtoJsonEdit(start, _end - start, text, start + selectFrom, selectLength);
    }

    private static ProtoJsonCompletion? Create(string text, List<Token> tokens, int next, Frame frame, (int Start, int End) replaced, bool isTyped, string prefix)
    {
        if (!frame.IsObject || frame.Type is not { } type || frame.State is not (State.Key or State.AfterValue))
        {
            return null;
        }

        // Keys after the caret in the same object.
        var present = new HashSet<string>(frame.Keys, StringComparer.Ordinal);
        var depth = 0;
        for (var i = next; i < tokens.Count && depth >= 0; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.BeginObject or TokenKind.BeginArray:
                    depth++;
                    break;
                case TokenKind.EndObject or TokenKind.EndArray:
                    depth--;
                    break;
                case TokenKind.String when depth == 0 && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Colon:
                    present.Add(tokens[i].Value(text));
                    break;
            }
        }

        var setOneofs = type.Fields
            .Where(f => f.OneofName is not null && (present.Contains(f.JsonName) || present.Contains(f.Name)))
            .Select(f => f.OneofName!)
            .ToHashSet(StringComparer.Ordinal);
        var available = type.Fields
            .Where(f => !present.Contains(f.JsonName) && !present.Contains(f.Name) && (f.OneofName is null || !setOneofs.Contains(f.OneofName)))
            .Where(f => f.JsonName.Contains(prefix, StringComparison.OrdinalIgnoreCase) || f.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.JsonName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

        var following = next < tokens.Count ? tokens[next] : (Token?)null;
        var isKeyName = isTyped && following is { Kind: TokenKind.Colon };
        var commaAfter = !isKeyName && following is { Kind: TokenKind.String or TokenKind.Literal or TokenKind.BeginObject or TokenKind.BeginArray };
        int? commaAt = frame.State == State.AfterValue ? frame.LastValueEnd : null;
        return new ProtoJsonCompletion(text, type, prefix, available, isTyped, replaced, isKeyName, commaAt, commaAfter);
    }

    // Applies one token to the open objects and arrays; false when the JSON before the caret cannot be followed.
    // root: the message type for the first token only (nothing may follow the root object).
    private static bool Step(Stack<Frame> stack, Token token, ProtoMessageType? root, string text)
    {
        stack.TryPeek(out var frame);
        switch (token.Kind)
        {
            case TokenKind.BeginObject:
                if (frame is null)
                {
                    if (root is null)
                    {
                        return false;
                    }

                    stack.Push(Frame.Object(root, null));
                    return true;
                }

                var field = ValueField(frame);
                stack.Push(ObjectFor(field, frame.IsObject));
                return true;
            case TokenKind.BeginArray:
                if (frame is null)
                {
                    return false;
                }

                var arrayField = frame.IsObject ? ValueField(frame) : null;
                stack.Push(Frame.Array(arrayField is { IsRepeated: true, IsMap: false } ? arrayField : null));
                return true;
            case TokenKind.EndObject or TokenKind.EndArray:
                if (frame is null || frame.IsObject != (token.Kind == TokenKind.EndObject))
                {
                    return false;
                }

                stack.Pop();
                if (stack.TryPeek(out var parent))
                {
                    parent.ValueDone(token.End);
                }

                return true;
            case TokenKind.Colon:
                if (frame is { IsObject: true, State: State.Colon })
                {
                    frame.State = State.Value;
                }

                return frame is not null;
            case TokenKind.Comma:
                if (frame is not null)
                {
                    frame.State = frame.IsObject ? State.Key : State.Value;
                }

                return frame is not null;
            default:
                if (frame is null)
                {
                    return false;
                }

                if (frame.IsObject && frame.State is State.Key or State.AfterValue)
                {
                    var key = token.Value(text);
                    frame.Keys.Add(key);
                    frame.Key = key;
                    frame.State = State.Colon;
                }
                else
                {
                    frame.ValueDone(token.End);
                }

                return true;
        }
    }

    // The field whose value starts at the current position of an object or array.
    private static ProtoField? ValueField(Frame frame)
    {
        if (!frame.IsObject)
        {
            return frame.Element;
        }

        if (frame.State != State.Value)
        {
            return null;
        }

        return frame.MapValue ?? (frame.Key is { } key ? frame.Type?.FindField(key) : null);
    }

    // The object written for a field: a message (its fields are offered), a map (any keys, typed values) or unknown.
    // isDirectValue: the object is the field's value in an object, not an element of its array.
    private static Frame ObjectFor(ProtoField? field, bool isDirectValue)
    {
        if (field is null || (isDirectValue && field.IsRepeated && !field.IsMap))
        {
            return Frame.Object(null, null);
        }

        if (field.IsMap)
        {
            return Frame.Object(null, isDirectValue ? field.MessageType!.FindField(2) : null);
        }

        return Frame.Object(field.Type == ProtoFieldType.Message && HasFields(field.MessageType!) ? field.MessageType : null, null);
    }

    // Well-known types written as strings, numbers, lists or free objects have no fields to offer.
    private static bool HasFields(ProtoMessageType type) =>
        !type.FullName.StartsWith("google.protobuf.", StringComparison.Ordinal);

    private static (string Value, int Offset, int Length) PlaceholderValue(ProtoField field)
    {
        if (field.IsMap)
        {
            return ("{}", 1, 0);
        }

        if (field.IsRepeated)
        {
            return ("[]", 1, 0);
        }

        if (field.Type == ProtoFieldType.Message)
        {
            return field.MessageType!.FullName switch
            {
                "google.protobuf.Timestamp" => Quoted("1970-01-01T00:00:00Z"),
                "google.protobuf.Duration" => Quoted("0s"),
                "google.protobuf.FieldMask" => Quoted(string.Empty),
                "google.protobuf.Value" => ("null", 0, 4),
                "google.protobuf.ListValue" => ("[]", 1, 0),
                "google.protobuf.Struct" or "google.protobuf.Any" or "google.protobuf.Empty" => ("{}", 1, 0),
                var name when name.StartsWith("google.protobuf.", StringComparison.Ordinal) && name.EndsWith("Value", StringComparison.Ordinal)
                    && field.MessageType.FindField(1) is { } wrapped => PlaceholderValue(wrapped),
                _ => ("{}", 1, 0),
            };
        }

        return field.Type switch
        {
            ProtoFieldType.Enum => Quoted(field.EnumType!.DefaultValue.Key),
            ProtoFieldType.Bool => ("false", 0, 5),
            ProtoFieldType.String or ProtoFieldType.Bytes => Quoted(string.Empty),
            ProtoFieldType.Int64 or ProtoFieldType.UInt64 or ProtoFieldType.SInt64 or ProtoFieldType.Fixed64 or ProtoFieldType.SFixed64 => Quoted("0"),
            _ => ("0", 0, 1),
        };

        static (string, int, int) Quoted(string value) => ("\"" + value + "\"", 1, value.Length);
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            switch (c)
            {
                case '{' when i + 1 < text.Length && text[i + 1] == '{':
                    // An unquoted {{variable}} is a value, not two objects.
                    var close = text.IndexOf("}}", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? text.Length : close + 2;
                    tokens.Add(new Token(TokenKind.Literal, start, i, true));
                    break;
                case '{':
                    tokens.Add(new Token(TokenKind.BeginObject, start, ++i, true));
                    break;
                case '}':
                    tokens.Add(new Token(TokenKind.EndObject, start, ++i, true));
                    break;
                case '[':
                    tokens.Add(new Token(TokenKind.BeginArray, start, ++i, true));
                    break;
                case ']':
                    tokens.Add(new Token(TokenKind.EndArray, start, ++i, true));
                    break;
                case ':':
                    tokens.Add(new Token(TokenKind.Colon, start, ++i, true));
                    break;
                case ',':
                    tokens.Add(new Token(TokenKind.Comma, start, ++i, true));
                    break;
                case '"':
                    i++;
                    var closed = false;
                    while (i < text.Length && text[i] is not ('\r' or '\n'))
                    {
                        if (text[i] == '\\')
                        {
                            i += 2;
                            continue;
                        }

                        if (text[i++] == '"')
                        {
                            closed = true;
                            break;
                        }
                    }

                    i = Math.Min(i, text.Length);
                    tokens.Add(new Token(TokenKind.String, start, i, closed));
                    break;
                default:
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '[' or ']' or ':' or ',' or '"'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Literal, start, i, true));
                    break;
            }
        }

        return tokens;
    }

    private enum TokenKind
    {
        BeginObject,
        EndObject,
        BeginArray,
        EndArray,
        Colon,
        Comma,
        String,
        Literal,
    }

    private enum State
    {
        Key,
        Colon,
        Value,
        AfterValue,
    }

    private readonly record struct Token(TokenKind Kind, int Start, int End, bool Closed)
    {
        // A string's content (escapes are rare in names and kept as written), or a literal's text.
        public string Value(string text) => Kind == TokenKind.String
            ? text[(Start + 1)..(Closed ? End - 1 : End)]
            : text[Start..End];
    }

    private sealed class Frame
    {
        private Frame(bool isObject) => IsObject = isObject;

        public bool IsObject { get; }

        /// <summary>The message whose fields are the keys of this object.</summary>
        public ProtoMessageType? Type { get; private init; }

        /// <summary>For a map object: the value field of every entry.</summary>
        public ProtoField? MapValue { get; private init; }

        /// <summary>For an array: the repeated field.</summary>
        public ProtoField? Element { get; private init; }

        public State State { get; set; }

        public string? Key { get; set; }

        public List<string> Keys { get; } = [];

        public int LastValueEnd { get; private set; }

        public static Frame Object(ProtoMessageType? type, ProtoField? mapValue) => new(true) { Type = type, MapValue = mapValue, State = State.Key };

        public static Frame Array(ProtoField? element) => new(false) { Element = element, State = State.Value };

        public void ValueDone(int end)
        {
            State = State.AfterValue;
            LastValueEnd = end;
        }
    }
}
