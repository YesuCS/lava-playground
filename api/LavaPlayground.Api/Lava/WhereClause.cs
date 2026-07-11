using System.Globalization;

namespace LavaPlayground.Api.Lava;

/// <summary>
/// Parses and evaluates Rock entity-command where clauses, e.g.
///   LastName == "Houston" && Age > 30
/// Operators: ==, !=, >, <, >=, <=, ^= (starts with), *= (contains).
/// Conjunctions: && and || (evaluated left to right, like Rock).
/// </summary>
public static class WhereClause
{
    private record Condition(string Property, string Op, object? Value);

    public static Func<object?, bool> Compile(string clause)
    {
        // Split into conditions and the conjunctions between them.
        var conditions = new List<Condition>();
        var conjunctions = new List<string>();

        var pos = 0;
        while (pos < clause.Length)
        {
            SkipWhitespace(clause, ref pos);
            if (pos >= clause.Length)
            {
                break;
            }
            conditions.Add(ParseCondition(clause, ref pos));
            SkipWhitespace(clause, ref pos);
            if (pos >= clause.Length)
            {
                break;
            }
            if (clause[pos..].StartsWith("&&"))
            {
                conjunctions.Add("&&");
                pos += 2;
            }
            else if (clause[pos..].StartsWith("||"))
            {
                conjunctions.Add("||");
                pos += 2;
            }
            else
            {
                throw new LavaException($"Expected && or || in where clause near \"{clause[pos..]}\".");
            }
        }

        if (conditions.Count == 0)
        {
            return _ => true;
        }

        return item =>
        {
            var result = Evaluate(conditions[0], item);
            for (var i = 0; i < conjunctions.Count && i + 1 < conditions.Count; i++)
            {
                var next = Evaluate(conditions[i + 1], item);
                result = conjunctions[i] == "&&" ? result && next : result || next;
            }
            return result;
        };
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
        {
            pos++;
        }
    }

    private static Condition ParseCondition(string clause, ref int pos)
    {
        // Property path
        var start = pos;
        while (pos < clause.Length && (char.IsLetterOrDigit(clause[pos]) || clause[pos] is '.' or '_'))
        {
            pos++;
        }
        var property = clause[start..pos];
        if (property.Length == 0)
        {
            throw new LavaException($"Expected a property name in where clause near \"{clause[start..]}\".");
        }

        SkipWhitespace(clause, ref pos);

        // Operator (two-char first)
        string[] twoChar = { "==", "!=", ">=", "<=", "^=", "*=" };
        var remaining = clause[pos..];
        string? op = twoChar.FirstOrDefault(o => remaining.StartsWith(o));
        if (op != null)
        {
            pos += 2;
        }
        else if (pos < clause.Length && (clause[pos] == '>' || clause[pos] == '<'))
        {
            op = clause[pos].ToString();
            pos += 1;
        }
        else
        {
            throw new LavaException($"Expected an operator (==, !=, >, <, >=, <=, ^=, *=) after \"{property}\".");
        }

        SkipWhitespace(clause, ref pos);

        // Value: quoted string, number, or true/false/null
        object? value;
        if (pos < clause.Length && (clause[pos] == '"' || clause[pos] == '\''))
        {
            var quote = clause[pos];
            pos++;
            var valueStart = pos;
            while (pos < clause.Length && clause[pos] != quote)
            {
                pos++;
            }
            if (pos >= clause.Length)
            {
                throw new LavaException("Unterminated string in where clause.");
            }
            value = clause[valueStart..pos];
            pos++;
        }
        else
        {
            var valueStart = pos;
            while (pos < clause.Length && !char.IsWhiteSpace(clause[pos]) && clause[pos] != '&' && clause[pos] != '|')
            {
                pos++;
            }
            var raw = clause[valueStart..pos];
            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) value = true;
            else if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) value = false;
            else if (raw.Equals("null", StringComparison.OrdinalIgnoreCase)) value = null;
            else if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)) value = number;
            else throw new LavaException($"Unrecognized value \"{raw}\" in where clause (quote strings).");
        }

        return new Condition(property, op, value);
    }

    private static bool Evaluate(Condition condition, object? item)
    {
        var actual = Resolve(item, condition.Property);
        return condition.Op switch
        {
            "==" => LavaValue.LooseEquals(actual, condition.Value),
            "!=" => !LavaValue.LooseEquals(actual, condition.Value),
            ">" => LavaValue.CompareNumeric(actual, condition.Value) > 0,
            "<" => LavaValue.CompareNumeric(actual, condition.Value) < 0,
            ">=" => LavaValue.CompareNumeric(actual, condition.Value) >= 0,
            "<=" => LavaValue.CompareNumeric(actual, condition.Value) <= 0,
            "^=" => LavaValue.ToDisplayString(actual).StartsWith(LavaValue.ToDisplayString(condition.Value), StringComparison.OrdinalIgnoreCase),
            "*=" => LavaValue.ToDisplayString(actual).Contains(LavaValue.ToDisplayString(condition.Value), StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static object? Resolve(object? item, string path)
    {
        var current = item;
        foreach (var segment in path.Split('.'))
        {
            if (current is IDictionary<string, object?> dict && dict.TryGetValue(segment, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }
        return current;
    }
}
