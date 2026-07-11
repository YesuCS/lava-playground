using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LavaPlayground.Api.Lava;

public record FilterInfo(string Name, string Category, string Description, string Example);

/// <summary>
/// The filter registry: standard Liquid filters plus a set of
/// Rock-RMS-flavored filters (Humanize, Possessive, NumberToOrdinal, ...).
/// </summary>
public class LavaFilterRegistry
{
    private readonly Dictionary<string, (FilterInfo Info, Func<object?, object?[], object?> Fn)> _filters =
        new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<FilterInfo> All => _filters.Values.Select(f => f.Info).OrderBy(f => f.Category).ThenBy(f => f.Name);

    public IEnumerable<string> Names => _filters.Keys;

    public object? Apply(string name, object? input, object?[] args)
    {
        if (!_filters.TryGetValue(name, out var filter))
        {
            throw new LavaException($"Unknown filter \"{name}\".");
        }
        try
        {
            return filter.Fn(input, args);
        }
        catch (LavaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LavaException($"The filter \"{name}\" failed: {ex.Message}");
        }
    }

    private void Register(string name, string category, string description, string example, Func<object?, object?[], object?> fn) =>
        _filters[name] = (new FilterInfo(name, category, description, example), fn);

    private static string Str(object? v) => LavaValue.ToDisplayString(v);

    private static decimal Num(object? v, string filterName)
    {
        if (!LavaValue.TryToNumber(v, out var n))
        {
            throw new LavaException($"\"{Str(v)}\" is not a number (in filter {filterName}).");
        }
        return n;
    }

    private static object? Arg(object?[] args, int index) => index < args.Length ? args[index] : null;

    public static LavaFilterRegistry CreateDefault()
    {
        var r = new LavaFilterRegistry();

        // ---------------- String filters ----------------
        r.Register("Upcase", "String", "Converts a string to uppercase.",
            "{{ 'hello' | Upcase }} → HELLO",
            (input, _) => Str(input).ToUpperInvariant());

        r.Register("Downcase", "String", "Converts a string to lowercase.",
            "{{ 'HELLO' | Downcase }} → hello",
            (input, _) => Str(input).ToLowerInvariant());

        r.Register("Capitalize", "String", "Capitalizes the first character of a string.",
            "{{ 'houston' | Capitalize }} → Houston",
            (input, _) =>
            {
                var s = Str(input);
                return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
            });

        r.Register("TitleCase", "String", "Capitalizes the first letter of each word.",
            "{{ 'the loop campus' | TitleCase }} → The Loop Campus",
            (input, _) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Str(input).ToLowerInvariant()));

        r.Register("SentenceCase", "String", "Capitalizes only the first letter of the sentence.",
            "{{ 'WELCOME HOME' | SentenceCase }} → Welcome home",
            (input, _) =>
            {
                var s = Str(input).ToLowerInvariant();
                return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
            });

        r.Register("Humanize", "String", "Turns camelCase or snake_case identifiers into readable text.",
            "{{ 'FirstTimeGuest' | Humanize }} → First time guest",
            (input, _) =>
            {
                var s = Str(input);
                s = Regex.Replace(s, "([a-z0-9])([A-Z])", "$1 $2");
                s = s.Replace('_', ' ').Replace('-', ' ');
                s = Regex.Replace(s, @"\s+", " ").Trim().ToLowerInvariant();
                return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
            });

        r.Register("Truncate", "String", "Shortens a string to n characters, appending an ellipsis if truncated.",
            "{{ 'Houston''s First' | Truncate:9 }} → Houston's…",
            (input, args) =>
            {
                var s = Str(input);
                var length = (int)Num(Arg(args, 0) ?? 50m, "Truncate");
                return s.Length <= length ? s : s[..length] + "…";
            });

        r.Register("Replace", "String", "Replaces every occurrence of a value in the string.",
            "{{ 'pew pew' | Replace:'pew','wow' }} → wow wow",
            (input, args) => Str(input).Replace(Str(Arg(args, 0)), Str(Arg(args, 1))));

        r.Register("Remove", "String", "Removes every occurrence of a value from the string.",
            "{{ 'Lava is hot hot' | Remove:' hot' }} → Lava is",
            (input, args) => Str(input).Replace(Str(Arg(args, 0)), string.Empty));

        r.Register("Append", "String", "Appends a value to the end of a string.",
            "{{ 'Houston' | Append:'''s First' }} → Houston's First",
            (input, args) => Str(input) + Str(Arg(args, 0)));

        r.Register("Prepend", "String", "Prepends a value to the start of a string.",
            "{{ 'First' | Prepend:'Houston''s ' }} → Houston's First",
            (input, args) => Str(Arg(args, 0)) + Str(input));

        r.Register("Split", "String", "Splits a string into an array on a separator.",
            "{{ 'a,b,c' | Split:',' | Size }} → 3",
            (input, args) => Str(input).Split(Str(Arg(args, 0))).Cast<object?>().ToList());

        r.Register("Trim", "String", "Removes leading and trailing whitespace.",
            "{{ '  hi  ' | Trim }} → hi",
            (input, _) => Str(input).Trim());

        r.Register("Right", "String", "Returns the rightmost n characters.",
            "{{ 'Houston' | Right:3 }} → ton",
            (input, args) =>
            {
                var s = Str(input);
                var n = (int)Num(Arg(args, 0) ?? 1m, "Right");
                return n >= s.Length ? s : s[^n..];
            });

        r.Register("Left", "String", "Returns the leftmost n characters.",
            "{{ 'Houston' | Left:3 }} → Hou",
            (input, args) =>
            {
                var s = Str(input);
                var n = (int)Num(Arg(args, 0) ?? 1m, "Left");
                return n >= s.Length ? s : s[..n];
            });

        r.Register("Slice", "String", "Returns a substring starting at an index (negative counts from the end).",
            "{{ 'Lava' | Slice:1,2 }} → av",
            (input, args) =>
            {
                var s = Str(input);
                var start = (int)Num(Arg(args, 0) ?? 0m, "Slice");
                if (start < 0) start = Math.Max(0, s.Length + start);
                if (start >= s.Length) return string.Empty;
                var len = args.Length > 1 ? (int)Num(args[1], "Slice") : 1;
                return s.Substring(start, Math.Min(len, s.Length - start));
            });

        r.Register("Pluralize", "String", "Returns the plural form of a singular English word.",
            "{{ 'person' | Pluralize }} → people",
            (input, _) => Pluralize(Str(input)));

        r.Register("PluralizeForQuantity", "String", "Pluralizes the word only when the quantity is not 1.",
            "{{ 'kid' | PluralizeForQuantity:3 }} → kids",
            (input, args) =>
            {
                var qty = Num(Arg(args, 0) ?? 1m, "PluralizeForQuantity");
                return qty == 1m ? Str(input) : Pluralize(Str(input));
            });

        r.Register("Possessive", "String", "Makes a name possessive, following English style rules.",
            "{{ 'Yesu' | Possessive }} → Yesu's",
            (input, _) =>
            {
                var s = Str(input);
                return s.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? s + "'" : s + "'s";
            });

        r.Register("ToCssClass", "String", "Lowercases a string and replaces non-alphanumerics with hyphens.",
            "{{ 'The Loop Campus' | ToCssClass }} → the-loop-campus",
            (input, _) =>
            {
                var s = Regex.Replace(Str(input).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
                return s;
            });

        r.Register("HtmlEncode", "String", "Encodes a string so it is safe to place in HTML.",
            "{{ '<b>hi</b>' | HtmlEncode }} → &lt;b&gt;hi&lt;/b&gt;",
            (input, _) => System.Net.WebUtility.HtmlEncode(Str(input)));

        // ---------------- Number filters ----------------
        r.Register("Plus", "Number", "Adds a number.",
            "{{ 3 | Plus:4 }} → 7",
            (input, args) => Num(input, "Plus") + Num(Arg(args, 0) ?? 0m, "Plus"));

        r.Register("Minus", "Number", "Subtracts a number.",
            "{{ 10 | Minus:4 }} → 6",
            (input, args) => Num(input, "Minus") - Num(Arg(args, 0) ?? 0m, "Minus"));

        r.Register("Times", "Number", "Multiplies by a number.",
            "{{ 6 | Times:7 }} → 42",
            (input, args) => Num(input, "Times") * Num(Arg(args, 0) ?? 1m, "Times"));

        r.Register("DividedBy", "Number", "Divides by a number.",
            "{{ 84 | DividedBy:2 }} → 42",
            (input, args) =>
            {
                var divisor = Num(Arg(args, 0) ?? 1m, "DividedBy");
                if (divisor == 0)
                {
                    throw new LavaException("Cannot divide by zero.");
                }
                return Num(input, "DividedBy") / divisor;
            });

        r.Register("Modulo", "Number", "Returns the remainder of a division.",
            "{{ 7 | Modulo:3 }} → 1",
            (input, args) => Num(input, "Modulo") % Num(Arg(args, 0) ?? 1m, "Modulo"));

        r.Register("Floor", "Number", "Rounds down to the nearest whole number.",
            "{{ 4.7 | Floor }} → 4",
            (input, _) => Math.Floor(Num(input, "Floor")));

        r.Register("Ceiling", "Number", "Rounds up to the nearest whole number.",
            "{{ 4.2 | Ceiling }} → 5",
            (input, _) => Math.Ceiling(Num(input, "Ceiling")));

        r.Register("Format", "Number", "Formats a number using a .NET format string.",
            "{{ 1234.5 | Format:'#,##0.00' }} → 1,234.50",
            (input, args) =>
            {
                var format = Str(Arg(args, 0));
                if (LavaValue.TryToNumber(input, out var n))
                {
                    return n.ToString(format, CultureInfo.InvariantCulture);
                }
                if (DateTime.TryParse(Str(input), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    return dt.ToString(format, CultureInfo.InvariantCulture);
                }
                return Str(input);
            });

        r.Register("NumberToOrdinal", "Number", "Converts a number to its ordinal form.",
            "{{ 3 | NumberToOrdinal }} → 3rd",
            (input, _) =>
            {
                var n = (long)Num(input, "NumberToOrdinal");
                var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch
                {
                    1 => "st",
                    2 => "nd",
                    3 => "rd",
                    _ => "th",
                };
                return n + suffix;
            });

        r.Register("NumberToOrdinalWords", "Number", "Converts a number to ordinal words.",
            "{{ 3 | NumberToOrdinalWords }} → third",
            (input, _) => NumberToOrdinalWords((int)Num(input, "NumberToOrdinalWords")));

        r.Register("NumberToWords", "Number", "Spells out a number in English words.",
            "{{ 3200 | NumberToWords }} → three thousand two hundred",
            (input, _) => NumberToWords((long)Num(input, "NumberToWords")));

        r.Register("NumberToRomanNumerals", "Number", "Converts a number (1-3999) to Roman numerals.",
            "{{ 2026 | NumberToRomanNumerals }} → MMXXVI",
            (input, _) => ToRoman((int)Num(input, "NumberToRomanNumerals")));

        r.Register("AsInteger", "Number", "Converts a value to a whole number.",
            "{{ '42.9' | AsInteger }} → 42",
            (input, _) => decimal.Truncate(Num(input, "AsInteger")));

        r.Register("AsDecimal", "Number", "Converts a value to a decimal number.",
            "{{ '3.14' | AsDecimal | Plus:1 }} → 4.14",
            (input, _) => Num(input, "AsDecimal"));

        // ---------------- Date filters ----------------
        r.Register("Date", "Date", "Formats a date using a .NET format string. The input 'Now' means the current time.",
            "{{ 'Now' | Date:'dddd, MMMM d, yyyy' }}",
            (input, args) =>
            {
                var format = Str(Arg(args, 0) ?? "M/d/yyyy");
                DateTime date;
                var s = Str(input);
                if (s.Equals("Now", StringComparison.OrdinalIgnoreCase))
                {
                    date = DateTime.Now;
                }
                else if (s.Equals("Today", StringComparison.OrdinalIgnoreCase))
                {
                    date = DateTime.Today;
                }
                else if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    return s; // Not a date; pass it through untouched.
                }
                return date.ToString(format, CultureInfo.InvariantCulture);
            });

        r.Register("DaysUntil", "Date", "Returns the number of whole days from today until the given date.",
            "{{ '2026-12-25' | DaysUntil }}",
            (input, _) =>
            {
                if (!DateTime.TryParse(Str(input), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    throw new LavaException($"\"{Str(input)}\" is not a date (in filter DaysUntil).");
                }
                return (decimal)(date.Date - DateTime.Today).TotalDays;
            });

        r.Register("HumanizeTimeSpan", "Date", "Describes how long ago (or from now) a date is, in friendly words.",
            "{{ '2020-01-01' | HumanizeTimeSpan }} → 6 years ago",
            (input, _) =>
            {
                if (!DateTime.TryParse(Str(input), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    throw new LavaException($"\"{Str(input)}\" is not a date (in filter HumanizeTimeSpan).");
                }
                var span = DateTime.Now - date;
                var future = span.TotalSeconds < 0;
                span = span.Duration();
                string text = span.TotalDays >= 365 ? Plural((int)(span.TotalDays / 365), "year")
                    : span.TotalDays >= 30 ? Plural((int)(span.TotalDays / 30), "month")
                    : span.TotalDays >= 1 ? Plural((int)span.TotalDays, "day")
                    : span.TotalHours >= 1 ? Plural((int)span.TotalHours, "hour")
                    : Plural(Math.Max(1, (int)span.TotalMinutes), "minute");
                return future ? "in " + text : text + " ago";

                static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
            });

        // ---------------- Array filters ----------------
        r.Register("Size", "Array", "Returns the number of items in an array or characters in a string.",
            "{{ Person.Groups | Size }}",
            (input, _) => input switch
            {
                string s => (decimal)s.Length,
                IEnumerable<object?> items => (decimal)items.Count(),
                _ => 0m,
            });

        r.Register("First", "Array", "Returns the first item of an array.",
            "{{ Person.Groups | First }}",
            (input, _) => input is not string and IEnumerable<object?> items ? items.FirstOrDefault() : input);

        r.Register("Last", "Array", "Returns the last item of an array.",
            "{{ Person.Groups | Last }}",
            (input, _) => input is not string and IEnumerable<object?> items ? items.LastOrDefault() : input);

        r.Register("Join", "Array", "Joins array items into a string with a separator.",
            "{{ Campuses | Map:'Name' | Join:', ' }}",
            (input, args) => input is not string and IEnumerable<object?> items
                ? string.Join(Str(Arg(args, 0) ?? ", "), items.Select(Str))
                : Str(input));

        r.Register("Map", "Array", "Plucks one property from each item in an array of objects.",
            "{{ Campuses | Map:'Name' | Join:', ' }}",
            (input, args) =>
            {
                var key = Str(Arg(args, 0));
                if (input is not IEnumerable<object?> items || input is string)
                {
                    return input;
                }
                return items
                    .Select(i => i is IDictionary<string, object?> d && d.TryGetValue(key, out var v) ? v : null)
                    .ToList();
            });

        r.Register("Sort", "Array", "Sorts an array, optionally by a property of each item.",
            "{{ Campuses | Sort:'Attendance' | Map:'Name' | Join:', ' }}",
            (input, args) =>
            {
                if (input is not IEnumerable<object?> items || input is string)
                {
                    return input;
                }
                var key = args.Length > 0 ? Str(args[0]) : null;
                object? KeyOf(object? item) => key != null && item is IDictionary<string, object?> d && d.TryGetValue(key, out var v) ? v : item;
                return items.OrderBy(KeyOf, Comparer<object?>.Create(LavaValue.CompareNumeric)).ToList();
            });

        r.Register("Reverse", "Array", "Reverses the order of an array.",
            "{{ Campuses | Reverse | Map:'Name' | Join:', ' }}",
            (input, _) => input is not string and IEnumerable<object?> items ? items.Reverse().ToList() : input);

        r.Register("Uniq", "Array", "Removes duplicate values from an array.",
            "{{ 'a,b,a' | Split:',' | Uniq | Join:',' }} → a,b",
            (input, _) =>
            {
                if (input is not IEnumerable<object?> items || input is string)
                {
                    return input;
                }
                var seen = new List<object?>();
                foreach (var item in items)
                {
                    if (!seen.Any(s => LavaValue.LooseEquals(s, item)))
                    {
                        seen.Add(item);
                    }
                }
                return seen;
            });

        r.Register("Where", "Array", "Filters an array of objects to items whose property equals a value.",
            "{{ Person.Groups | Where:'Role','Leader' | Size }}",
            (input, args) =>
            {
                if (input is not IEnumerable<object?> items || input is string)
                {
                    return input;
                }
                var key = Str(Arg(args, 0));
                var expected = Arg(args, 1);
                return items
                    .Where(i => i is IDictionary<string, object?> d && d.TryGetValue(key, out var v) && LavaValue.LooseEquals(v, expected))
                    .ToList();
            });

        // ---------------- Logic filters ----------------
        r.Register("Default", "Logic", "Returns a fallback when the input is null, false, or an empty string.",
            "{{ Person.MiddleName | Default:'(none)' }}",
            (input, args) => input == null || input as bool? == false || (input is string s && s.Length == 0)
                ? Arg(args, 0)
                : input);

        return r;
    }

    // -----------------------------------------------------------------------
    // English helpers
    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, string> Irregulars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"] = "people",
        ["child"] = "children",
        ["man"] = "men",
        ["woman"] = "women",
        ["foot"] = "feet",
        ["tooth"] = "teeth",
        ["goose"] = "geese",
        ["mouse"] = "mice",
        ["deacon"] = "deacons",
        ["staff"] = "staff",
        ["sheep"] = "sheep",
    };

    private static string Pluralize(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }
        if (Irregulars.TryGetValue(word, out var irregular))
        {
            return MatchCase(word, irregular);
        }
        var lower = word.ToLowerInvariant();
        if (lower.EndsWith("y") && word.Length > 1 && !"aeiou".Contains(lower[^2]))
        {
            return word[..^1] + "ies";
        }
        if (lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z") || lower.EndsWith("ch") || lower.EndsWith("sh"))
        {
            return word + "es";
        }
        return word + "s";
    }

    private static string MatchCase(string original, string replacement) =>
        original.Length > 0 && char.IsUpper(original[0]) && replacement.Length > 0
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;

    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens =
    {
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
    };

    private static string NumberToWords(long n)
    {
        if (n < 0)
        {
            return "negative " + NumberToWords(-n);
        }
        if (n < 20)
        {
            return Ones[n];
        }
        if (n < 100)
        {
            return Tens[n / 10] + (n % 10 > 0 ? "-" + Ones[n % 10] : string.Empty);
        }
        if (n < 1000)
        {
            return Ones[n / 100] + " hundred" + (n % 100 > 0 ? " " + NumberToWords(n % 100) : string.Empty);
        }
        if (n < 1_000_000)
        {
            return NumberToWords(n / 1000) + " thousand" + (n % 1000 > 0 ? " " + NumberToWords(n % 1000) : string.Empty);
        }
        if (n < 1_000_000_000)
        {
            return NumberToWords(n / 1_000_000) + " million" + (n % 1_000_000 > 0 ? " " + NumberToWords(n % 1_000_000) : string.Empty);
        }
        return NumberToWords(n / 1_000_000_000) + " billion" + (n % 1_000_000_000 > 0 ? " " + NumberToWords(n % 1_000_000_000) : string.Empty);
    }

    private static readonly Dictionary<string, string> OrdinalWordOverrides = new()
    {
        ["one"] = "first",
        ["two"] = "second",
        ["three"] = "third",
        ["five"] = "fifth",
        ["eight"] = "eighth",
        ["nine"] = "ninth",
        ["twelve"] = "twelfth",
    };

    private static string NumberToOrdinalWords(int n)
    {
        var words = NumberToWords(n);
        var lastSpace = Math.Max(words.LastIndexOf(' '), words.LastIndexOf('-'));
        var head = lastSpace == -1 ? string.Empty : words[..(lastSpace + 1)];
        var last = lastSpace == -1 ? words : words[(lastSpace + 1)..];

        if (OrdinalWordOverrides.TryGetValue(last, out var replaced))
        {
            return head + replaced;
        }
        if (last.EndsWith("y"))
        {
            return head + last[..^1] + "ieth";
        }
        return head + last + "th";
    }

    private static string ToRoman(int n)
    {
        if (n is < 1 or > 3999)
        {
            throw new LavaException("NumberToRomanNumerals supports 1 through 3999.");
        }
        var map = new (int Value, string Symbol)[]
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
            (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        };
        var sb = new StringBuilder();
        foreach (var (value, symbol) in map)
        {
            while (n >= value)
            {
                sb.Append(symbol);
                n -= value;
            }
        }
        return sb.ToString();
    }
}
