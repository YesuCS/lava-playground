using System.Globalization;
using System.Text;

namespace LavaPlayground.Api.Lava;

// ---------------------------------------------------------------------------
// Render context: a stack of variable scopes plus the filter registry.
// ---------------------------------------------------------------------------

public class RenderContext
{
    private readonly List<Dictionary<string, object?>> _scopes = new();

    public LavaFilterRegistry Filters { get; }

    public RenderContext(Dictionary<string, object?> root, LavaFilterRegistry filters)
    {
        _scopes.Add(new Dictionary<string, object?>(root, StringComparer.OrdinalIgnoreCase));
        Filters = filters;
    }

    public void PushScope() => _scopes.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

    public void PopScope() => _scopes.RemoveAt(_scopes.Count - 1);

    /// <summary>Sets a variable in the innermost scope.</summary>
    public void Set(string name, object? value) => _scopes[^1][name] = value;

    /// <summary>Sets a variable in the outermost (template-global) scope, as {% assign %} does.</summary>
    public void SetGlobal(string name, object? value) => _scopes[0][name] = value;

    public bool TryResolve(string name, out object? value)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(name, out value))
            {
                return true;
            }
        }
        value = null;
        return false;
    }
}

// ---------------------------------------------------------------------------
// Tokenizer for the *inside* of {{ ... }} and {% ... %} markers.
// ---------------------------------------------------------------------------

public enum TokenType { Identifier, String, Number, Operator, Pipe, Colon, Comma, Dot, LBracket, RBracket, Assign, End }

public readonly record struct Token(TokenType Type, string Text);

public class ExpressionTokenizer
{
    private readonly List<Token> _tokens = new();
    private int _index;

    public ExpressionTokenizer(string input)
    {
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c is '\'' or '"')
            {
                var quote = c;
                var sb = new StringBuilder();
                i++;
                while (i < input.Length && input[i] != quote)
                {
                    sb.Append(input[i]);
                    i++;
                }
                if (i >= input.Length)
                {
                    throw new LavaException($"Unterminated string literal near \"{input}\".");
                }
                i++; // closing quote
                _tokens.Add(new Token(TokenType.String, sb.ToString()));
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && i + 1 < input.Length && char.IsDigit(input[i + 1])))
            {
                var start = i;
                i++;
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.'))
                {
                    i++;
                }
                _tokens.Add(new Token(TokenType.Number, input[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                {
                    i++;
                }
                _tokens.Add(new Token(TokenType.Identifier, input[start..i]));
                continue;
            }

            switch (c)
            {
                case '|': _tokens.Add(new Token(TokenType.Pipe, "|")); i++; break;
                case ':': _tokens.Add(new Token(TokenType.Colon, ":")); i++; break;
                case ',': _tokens.Add(new Token(TokenType.Comma, ",")); i++; break;
                case '.': _tokens.Add(new Token(TokenType.Dot, ".")); i++; break;
                case '[': _tokens.Add(new Token(TokenType.LBracket, "[")); i++; break;
                case ']': _tokens.Add(new Token(TokenType.RBracket, "]")); i++; break;
                case '=' when i + 1 < input.Length && input[i + 1] == '=':
                    _tokens.Add(new Token(TokenType.Operator, "==")); i += 2; break;
                case '=':
                    _tokens.Add(new Token(TokenType.Assign, "=")); i++; break;
                case '!' when i + 1 < input.Length && input[i + 1] == '=':
                    _tokens.Add(new Token(TokenType.Operator, "!=")); i += 2; break;
                case '>' when i + 1 < input.Length && input[i + 1] == '=':
                    _tokens.Add(new Token(TokenType.Operator, ">=")); i += 2; break;
                case '<' when i + 1 < input.Length && input[i + 1] == '=':
                    _tokens.Add(new Token(TokenType.Operator, "<=")); i += 2; break;
                case '>': _tokens.Add(new Token(TokenType.Operator, ">")); i++; break;
                case '<': _tokens.Add(new Token(TokenType.Operator, "<")); i++; break;
                default:
                    throw new LavaException($"Unexpected character '{c}' in expression \"{input}\".");
            }
        }
        _tokens.Add(new Token(TokenType.End, string.Empty));
    }

    public Token Peek() => _tokens[_index];

    public Token Next() => _tokens[_index++];

    public Token Expect(TokenType type, string context)
    {
        var token = Next();
        if (token.Type != type)
        {
            throw new LavaException($"Expected {type} but found \"{token.Text}\" while parsing {context}.");
        }
        return token;
    }

    public bool TryConsume(TokenType type)
    {
        if (Peek().Type != type)
        {
            return false;
        }
        _index++;
        return true;
    }

    public bool TryConsumeIdentifier(string text)
    {
        if (Peek().Type == TokenType.Identifier && string.Equals(Peek().Text, text, StringComparison.OrdinalIgnoreCase))
        {
            _index++;
            return true;
        }
        return false;
    }
}

// ---------------------------------------------------------------------------
// Primary expressions: literals and dotted/bracketed variable paths.
// ---------------------------------------------------------------------------

public abstract class Primary
{
    public abstract object? Evaluate(RenderContext context);

    public static Primary Parse(ExpressionTokenizer t)
    {
        var token = t.Next();
        switch (token.Type)
        {
            case TokenType.String:
                return new Literal(token.Text);
            case TokenType.Number:
                return new Literal(decimal.Parse(token.Text, CultureInfo.InvariantCulture));
            case TokenType.Identifier:
                if (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase)) return new Literal(true);
                if (string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase)) return new Literal(false);
                if (string.Equals(token.Text, "null", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(token.Text, "nil", StringComparison.OrdinalIgnoreCase)) return new Literal(null);
                return VariablePath.ParseRest(t, token.Text);
            default:
                throw new LavaException($"Unexpected token \"{token.Text}\" where a value was expected.");
        }
    }
}

public class Literal : Primary
{
    private readonly object? _value;
    public Literal(object? value) => _value = value;
    public override object? Evaluate(RenderContext context) => _value;
}

public class VariablePath : Primary
{
    // Each segment is either a string property name or an int index.
    private readonly List<object> _segments;

    private VariablePath(List<object> segments) => _segments = segments;

    public static VariablePath ParseRest(ExpressionTokenizer t, string rootName)
    {
        var segments = new List<object> { rootName };
        while (true)
        {
            if (t.TryConsume(TokenType.Dot))
            {
                segments.Add(t.Expect(TokenType.Identifier, "a property name").Text);
            }
            else if (t.TryConsume(TokenType.LBracket))
            {
                var idx = t.Next();
                segments.Add(idx.Type switch
                {
                    TokenType.Number => (object)int.Parse(idx.Text, CultureInfo.InvariantCulture),
                    TokenType.String => idx.Text,
                    _ => throw new LavaException($"Invalid index \"{idx.Text}\"; use a number or a quoted key."),
                });
                t.Expect(TokenType.RBracket, "an index");
            }
            else
            {
                break;
            }
        }
        return new VariablePath(segments);
    }

    public override object? Evaluate(RenderContext context)
    {
        if (!context.TryResolve((string)_segments[0], out var current))
        {
            return null;
        }

        foreach (var segment in _segments.Skip(1))
        {
            current = Access(current, segment);
            if (current == null)
            {
                return null;
            }
        }
        return current;
    }

    private static object? Access(object? target, object segment)
    {
        switch (target)
        {
            case IDictionary<string, object?> dict when segment is string name:
                return dict.TryGetValue(name, out var v) ? v : null;
            case IList<object?> list when segment is int index:
                return index >= 0 && index < list.Count ? list[index] : null;
            case string s when segment is int index:
                return index >= 0 && index < s.Length ? s[index].ToString() : null;
            default:
                return null;
        }
    }
}

// ---------------------------------------------------------------------------
// Filtered expressions:  primary | FilterOne | FilterTwo:'arg1',arg2
// ---------------------------------------------------------------------------

public class FilterCall
{
    public string Name { get; }
    public List<Primary> Arguments { get; }

    public FilterCall(string name, List<Primary> arguments)
    {
        Name = name;
        Arguments = arguments;
    }
}

public class FilteredExpression
{
    private readonly Primary _primary;
    private readonly List<FilterCall> _filters;

    private FilteredExpression(Primary primary, List<FilterCall> filters)
    {
        _primary = primary;
        _filters = filters;
    }

    public static FilteredExpression Parse(string input)
    {
        var t = new ExpressionTokenizer(input);
        var expr = Parse(t);
        if (t.Peek().Type != TokenType.End)
        {
            throw new LavaException($"Unexpected \"{t.Peek().Text}\" at the end of expression \"{input}\".");
        }
        return expr;
    }

    public static FilteredExpression Parse(ExpressionTokenizer t)
    {
        var primary = Primary.Parse(t);
        var filters = new List<FilterCall>();
        while (t.TryConsume(TokenType.Pipe))
        {
            var name = t.Expect(TokenType.Identifier, "a filter name").Text;
            var args = new List<Primary>();
            if (t.TryConsume(TokenType.Colon))
            {
                args.Add(Primary.Parse(t));
                while (t.TryConsume(TokenType.Comma))
                {
                    args.Add(Primary.Parse(t));
                }
            }
            filters.Add(new FilterCall(name, args));
        }
        return new FilteredExpression(primary, filters);
    }

    public object? Evaluate(RenderContext context)
    {
        var value = _primary.Evaluate(context);
        foreach (var filter in _filters)
        {
            var args = filter.Arguments.Select(a => a.Evaluate(context)).ToArray();
            value = context.Filters.Apply(filter.Name, value, args);
        }
        return value;
    }
}

// ---------------------------------------------------------------------------
// Boolean conditions for {% if %} / {% elsif %}.
// Liquid-style: no parentheses; 'and' binds tighter than 'or'.
// ---------------------------------------------------------------------------

public abstract class Condition
{
    public abstract bool Evaluate(RenderContext context);

    public static Condition Parse(string input)
    {
        var t = new ExpressionTokenizer(input);
        var condition = ParseOr(t);
        if (t.Peek().Type != TokenType.End)
        {
            throw new LavaException($"Unexpected \"{t.Peek().Text}\" at the end of condition \"{input}\".");
        }
        return condition;
    }

    private static Condition ParseOr(ExpressionTokenizer t)
    {
        var left = ParseAnd(t);
        while (t.TryConsumeIdentifier("or"))
        {
            left = new BinaryCondition(left, ParseAnd(t), isAnd: false);
        }
        return left;
    }

    private static Condition ParseAnd(ExpressionTokenizer t)
    {
        var left = ParseComparison(t);
        while (t.TryConsumeIdentifier("and"))
        {
            left = new BinaryCondition(left, ParseComparison(t), isAnd: true);
        }
        return left;
    }

    private static Condition ParseComparison(ExpressionTokenizer t)
    {
        var left = FilteredExpression.Parse(t);
        if (t.Peek().Type == TokenType.Operator)
        {
            var op = t.Next().Text;
            return new Comparison(left, op, FilteredExpression.Parse(t));
        }
        if (t.TryConsumeIdentifier("contains"))
        {
            return new Comparison(left, "contains", FilteredExpression.Parse(t));
        }
        return new Truthiness(left);
    }
}

public class Truthiness : Condition
{
    private readonly FilteredExpression _expr;
    public Truthiness(FilteredExpression expr) => _expr = expr;
    public override bool Evaluate(RenderContext context) => LavaValue.IsTruthy(_expr.Evaluate(context));
}

public class BinaryCondition : Condition
{
    private readonly Condition _left;
    private readonly Condition _right;
    private readonly bool _isAnd;

    public BinaryCondition(Condition left, Condition right, bool isAnd)
    {
        _left = left;
        _right = right;
        _isAnd = isAnd;
    }

    public override bool Evaluate(RenderContext context) => _isAnd
        ? _left.Evaluate(context) && _right.Evaluate(context)
        : _left.Evaluate(context) || _right.Evaluate(context);
}

public class Comparison : Condition
{
    private readonly FilteredExpression _left;
    private readonly string _op;
    private readonly FilteredExpression _right;

    public Comparison(FilteredExpression left, string op, FilteredExpression right)
    {
        _left = left;
        _op = op;
        _right = right;
    }

    public override bool Evaluate(RenderContext context)
    {
        var l = _left.Evaluate(context);
        var r = _right.Evaluate(context);

        return _op switch
        {
            "==" => LavaValue.LooseEquals(l, r),
            "!=" => !LavaValue.LooseEquals(l, r),
            ">" => LavaValue.CompareNumeric(l, r) > 0,
            "<" => LavaValue.CompareNumeric(l, r) < 0,
            ">=" => LavaValue.CompareNumeric(l, r) >= 0,
            "<=" => LavaValue.CompareNumeric(l, r) <= 0,
            "contains" => Contains(l, r),
            _ => throw new LavaException($"Unknown operator \"{_op}\"."),
        };
    }

    private static bool Contains(object? haystack, object? needle) => haystack switch
    {
        string s => s.Contains(LavaValue.ToDisplayString(needle), StringComparison.OrdinalIgnoreCase),
        IEnumerable<object?> items => items.Any(i => LavaValue.LooseEquals(i, needle)),
        _ => false,
    };
}

// ---------------------------------------------------------------------------
// Value semantics shared across the engine.
// ---------------------------------------------------------------------------

public static class LavaValue
{
    /// <summary>Liquid truthiness: only null and false are falsy.</summary>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        _ => true,
    };

    public static bool TryToNumber(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal d: number = d; return true;
            case int i: number = i; return true;
            case long l: number = l; return true;
            case double dbl: number = (decimal)dbl; return true;
            case string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                number = parsed; return true;
            default:
                number = 0; return false;
        }
    }

    public static bool LooseEquals(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        if (left is bool lb && right is bool rb) return lb == rb;
        if (TryToNumber(left, out var ln) && TryToNumber(right, out var rn) && left is not string && right is not string)
        {
            return ln == rn;
        }
        return string.Equals(ToDisplayString(left), ToDisplayString(right), StringComparison.Ordinal);
    }

    public static int CompareNumeric(object? left, object? right)
    {
        if (TryToNumber(left, out var ln) && TryToNumber(right, out var rn))
        {
            return ln.CompareTo(rn);
        }
        return string.Compare(ToDisplayString(left), ToDisplayString(right), StringComparison.Ordinal);
    }

    public static string ToDisplayString(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case bool b:
                return b ? "true" : "false";
            case decimal d:
                return d == decimal.Truncate(d) && Math.Abs(d) < long.MaxValue
                    ? ((long)d).ToString(CultureInfo.InvariantCulture)
                    : d.ToString(CultureInfo.InvariantCulture);
            case DateTime dt:
                return dt.ToString("M/d/yyyy h:mm tt", CultureInfo.InvariantCulture);
            case string s:
                return s;
            case IDictionary<string, object?> dict:
                return "{" + string.Join(", ", dict.Select(kv => $"{kv.Key}: {ToDisplayString(kv.Value)}")) + "}";
            case IEnumerable<object?> items:
                return string.Concat(items.Select(ToDisplayString));
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
