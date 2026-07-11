using System.Globalization;
using System.Text;

namespace LavaPlayground.Api.Lava;

public record FilterInfo(string Name, string Category, string Description, string Example);

/// <summary>
/// The filter registry. Aims for full coverage of Rock RMS's Lava filters
/// that can run without a Rock database: Text, Numeric, Date, Collection,
/// Type-coercion, and Color categories. Entity-bound filters (Attribute,
/// Address, PersonById, ...) are only available in remote mode, where the
/// template renders on a real Rock server.
///
/// Registration is split across partial class files by category:
///   LavaFilters.Strings.cs, .Numbers.cs, .Dates.cs, .Arrays.cs,
///   .Colors.cs, .Misc.cs
/// </summary>
public partial class LavaFilterRegistry
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

    public static LavaFilterRegistry CreateDefault()
    {
        var r = new LavaFilterRegistry();
        r.RegisterStringFilters();
        r.RegisterNumberFilters();
        r.RegisterDateFilters();
        r.RegisterArrayFilters();
        r.RegisterColorFilters();
        r.RegisterMiscFilters();
        return r;
    }

    // -----------------------------------------------------------------------
    // Shared helpers for the category files
    // -----------------------------------------------------------------------

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

    /// <summary>Returns the input as a list, or null when it isn't a collection.</summary>
    private static List<object?>? AsList(object? input) => input switch
    {
        string => null,
        IDictionary<string, object?> => null,
        IEnumerable<object?> items => items.ToList(),
        _ => null,
    };

    /// <summary>Resolves a dotted property path (e.g. "GroupRole.Name") on an object tree.</summary>
    private static object? PropValue(object? item, string path)
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

    private static DateTime ToDate(object? value, string filterName)
    {
        if (!TryToDate(value, out var date))
        {
            throw new LavaException($"\"{Str(value)}\" is not a date (in filter {filterName}).");
        }
        return date;
    }

    private static bool TryToDate(object? value, out DateTime date)
    {
        switch (value)
        {
            case DateTime dt:
                date = dt;
                return true;
            case string s when s.Equals("Now", StringComparison.OrdinalIgnoreCase):
                date = DateTime.Now;
                return true;
            case string s when s.Equals("Today", StringComparison.OrdinalIgnoreCase):
                date = DateTime.Today;
                return true;
            case string s:
                return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                    || DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
            default:
                date = default;
                return false;
        }
    }

    // -----------------------------------------------------------------------
    // English language helpers
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
        ["staff"] = "staff",
        ["sheep"] = "sheep",
    };

    private static readonly Dictionary<string, string> IrregularsReversed =
        Irregulars.Where(kv => kv.Key != kv.Value)
            .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

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

    private static string Singularize(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }
        if (IrregularsReversed.TryGetValue(word, out var irregular))
        {
            return MatchCase(word, irregular);
        }
        var lower = word.ToLowerInvariant();
        if (lower.EndsWith("ies") && word.Length > 3)
        {
            return word[..^3] + "y";
        }
        if (lower.EndsWith("ches") || lower.EndsWith("shes") || lower.EndsWith("xes") || lower.EndsWith("zes") || lower.EndsWith("ses"))
        {
            return word[..^2];
        }
        if (lower.EndsWith("s") && !lower.EndsWith("ss"))
        {
            return word[..^1];
        }
        return word;
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

    private static string NumberToOrdinal(long n)
    {
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return n + suffix;
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
