namespace LavaPlayground.Api.Lava;

public partial class LavaFilterRegistry
{
    private void RegisterArrayFilters()
    {
        Register("Size", "Collection", "Returns the number of items in an array or characters in a string.",
            "{{ Person.Groups | Size }}",
            (input, _) => input switch
            {
                string s => (decimal)s.Length,
                IDictionary<string, object?> d => (decimal)d.Count,
                IEnumerable<object?> items => (decimal)items.Count(),
                _ => 0m,
            });

        Register("First", "Collection", "Returns the first item of an array.",
            "{% assign g = Person.Groups | First %}{{ g.Name }}",
            (input, _) => AsList(input) is { } list ? list.FirstOrDefault() : input);

        Register("Last", "Collection", "Returns the last item of an array.",
            "{% assign g = Person.Groups | Last %}{{ g.Name }}",
            (input, _) => AsList(input) is { } list ? list.LastOrDefault() : input);

        Register("Index", "Collection", "Returns the item at a zero-based index.",
            "{% assign g = Person.Groups | Index:1 %}{{ g.Name }}",
            (input, args) =>
            {
                var index = (int)Num(Arg(args, 0) ?? 0m, "Index");
                var list = AsList(input);
                return list != null && index >= 0 && index < list.Count ? list[index] : null;
            });

        Register("Take", "Collection", "Returns the first n items.",
            "{{ Campuses | Take:2 | Map:'Name' | Join:',' }}",
            (input, args) => AsList(input) is { } list
                ? list.Take((int)Num(Arg(args, 0) ?? 0m, "Take")).ToList()
                : input);

        Register("Skip", "Collection", "Skips the first n items.",
            "{{ Campuses | Skip:1 | Map:'Name' | Join:',' }}",
            (input, args) => AsList(input) is { } list
                ? list.Skip((int)Num(Arg(args, 0) ?? 0m, "Skip")).ToList()
                : input);

        Register("Join", "Collection", "Joins array items into a string with a separator.",
            "{{ Campuses | Map:'Name' | Join:', ' }}",
            (input, args) => AsList(input) is { } list
                ? string.Join(Str(Arg(args, 0) ?? ", "), list.Select(Str))
                : Str(input));

        Register("Select", "Collection", "Projects one property from each item (supports dotted paths).",
            "{{ Campuses | Select:'Name' | Join:', ' }}",
            (input, args) => AsList(input) is { } list
                ? list.Select(i => PropValue(i, Str(Arg(args, 0)))).ToList()
                : input);

        Register("Map", "Collection", "Projects one property from each item (alias of Select).",
            "{{ Campuses | Map:'Name' | Join:', ' }}",
            (input, args) => AsList(input) is { } list
                ? list.Select(i => PropValue(i, Str(Arg(args, 0)))).ToList()
                : input);

        Register("Sort", "Collection", "Sorts an array by a property; pass 'desc' as a second argument for descending.",
            "{{ Campuses | Sort:'Attendance','desc' | Map:'Name' | First }}",
            (input, args) =>
            {
                if (AsList(input) is not { } list)
                {
                    return input;
                }
                var key = args.Length > 0 ? Str(args[0]) : null;
                var descending = args.Length > 1 && Str(args[1]).Equals("desc", StringComparison.OrdinalIgnoreCase);
                object? KeyOf(object? item) => key != null ? PropValue(item, key) : item;
                var comparer = Comparer<object?>.Create(LavaValue.CompareNumeric);
                var sorted = descending
                    ? list.OrderByDescending(KeyOf, comparer)
                    : list.OrderBy(KeyOf, comparer);
                return sorted.ToList();
            });

        Register("Reverse", "Collection", "Reverses the order of an array.",
            "{{ Campuses | Reverse | Map:'Name' | First }}",
            (input, _) => AsList(input) is { } list ? Enumerable.Reverse(list).ToList() : input);

        Register("Shuffle", "Collection", "Randomizes the order of an array.",
            "{{ Campuses | Shuffle | Map:'Name' | First }}",
            (input, _) => AsList(input) is { } list ? list.OrderBy(_ => Random.Shared.Next()).ToList() : input);

        Register("Uniq", "Collection", "Removes duplicate primitive values from an array.",
            "{{ 'a,b,a' | Split:',' | Uniq | Join:',' }} → a,b",
            (input, _) => AsList(input) is { } list ? DistinctBy(list, item => item) : input);

        Register("Distinct", "Collection", "Removes duplicates; with a property argument, de-dupes by that property.",
            "{{ Campuses | Distinct:'City' | Size }}",
            (input, args) =>
            {
                if (AsList(input) is not { } list)
                {
                    return input;
                }
                var key = args.Length > 0 ? Str(args[0]) : null;
                return DistinctBy(list, item => key != null ? PropValue(item, key) : item);
            });

        Register("Compact", "Collection", "Removes null and empty values from an array.",
            "{{ values | Compact | Size }}",
            (input, _) => AsList(input) is { } list
                ? list.Where(i => i != null && !(i is string s && s.Length == 0)).ToList()
                : input);

        Register("Concat", "Collection", "Combines two arrays.",
            "{{ listA | Concat:listB | Size }}",
            (input, args) =>
            {
                var left = AsList(input) ?? new List<object?>();
                var right = AsList(Arg(args, 0)) ?? new List<object?>();
                return left.Concat(right).ToList();
            });

        Register("Contains", "Collection", "Returns true when the array contains the value.",
            "{{ names | Contains:'Sam' }}",
            (input, args) =>
            {
                var needle = Arg(args, 0);
                return input switch
                {
                    string s => s.Contains(Str(needle), StringComparison.OrdinalIgnoreCase),
                    not string and IEnumerable<object?> items => items.Any(i => LavaValue.LooseEquals(i, needle)),
                    _ => false,
                };
            });

        Register("Sum", "Collection", "Sums an array of numbers; with a property argument, sums that property.",
            "{{ Campuses | Sum:'Attendance' }}",
            (input, args) =>
            {
                if (AsList(input) is not { } list)
                {
                    return Num(input, "Sum");
                }
                var key = args.Length > 0 ? Str(args[0]) : null;
                return list.Sum(i =>
                {
                    var value = key != null ? PropValue(i, key) : i;
                    return LavaValue.TryToNumber(value, out var n) ? n : 0m;
                });
            });

        Register("Where", "Collection", "Filters an array of objects to items whose property equals a value.",
            "{{ Person.Groups | Where:'Role','Leader' | Size }}",
            (input, args) =>
            {
                if (AsList(input) is not { } list)
                {
                    return input;
                }
                var key = Str(Arg(args, 0));
                var expected = Arg(args, 1);
                return list.Where(i => LavaValue.LooseEquals(PropValue(i, key), expected)).ToList();
            });

        Register("GroupBy", "Collection", "Groups an array by a property, returning a dictionary you can loop over.",
            "{% assign byCity = Campuses | GroupBy:'City' %}",
            (input, args) =>
            {
                if (AsList(input) is not { } list)
                {
                    return input;
                }
                var key = Str(Arg(args, 0));
                var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in list)
                {
                    var groupKey = Str(PropValue(item, key));
                    if (result.TryGetValue(groupKey, out var existing) && existing is List<object?> bucket)
                    {
                        bucket.Add(item);
                    }
                    else
                    {
                        result[groupKey] = new List<object?> { item };
                    }
                }
                return result;
            });

        Register("PropertyToKeyValue", "Collection", "Splits a dictionary entry into .Key and .Value (used with GroupBy).",
            "{% assign kv = entry | PropertyToKeyValue %}{{ kv.Key }}",
            (input, _) => input);

        Register("AddToArray", "Collection", "Appends a value to an array, creating the array when needed.",
            "{% assign ids = ids | AddToArray:item.Id %}",
            (input, args) =>
            {
                var value = Arg(args, 0);
                if (AsList(input) is { } list)
                {
                    return list.Append(value).ToList();
                }
                if (input == null || (input is string s && s.Length == 0))
                {
                    return new List<object?> { value };
                }
                return new List<object?> { input, value };
            });

        Register("RemoveFromArray", "Collection", "Removes every occurrence of a value from an array.",
            "{{ names | RemoveFromArray:'Sam' | Size }}",
            (input, args) =>
            {
                var value = Arg(args, 0);
                return AsList(input) is { } list
                    ? list.Where(i => !LavaValue.LooseEquals(i, value)).ToList()
                    : input;
            });

        Register("AddToDictionary", "Collection", "Adds a key/value pair to a dictionary, creating it when needed.",
            "{% assign d = d | AddToDictionary:'key','value' %}",
            (input, args) =>
            {
                var dict = input is IDictionary<string, object?> existing
                    ? new Dictionary<string, object?>(existing, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                dict[Str(Arg(args, 0))] = Arg(args, 1);
                return dict;
            });

        Register("AllKeysFromDictionary", "Collection", "Returns a dictionary's keys as an array.",
            "{{ d | AllKeysFromDictionary | Join:',' }}",
            (input, _) => input is IDictionary<string, object?> dict
                ? dict.Keys.Cast<object?>().ToList()
                : new List<object?>());
    }

    private static List<object?> DistinctBy(List<object?> list, Func<object?, object?> keyOf)
    {
        var result = new List<object?>();
        var seenKeys = new List<object?>();
        foreach (var item in list)
        {
            var key = keyOf(item);
            if (!seenKeys.Any(k => LavaValue.LooseEquals(k, key)))
            {
                seenKeys.Add(key);
                result.Add(item);
            }
        }
        return result;
    }
}
