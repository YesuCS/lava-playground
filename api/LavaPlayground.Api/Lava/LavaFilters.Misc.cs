using System.Text.Json;

namespace LavaPlayground.Api.Lava;

public partial class LavaFilterRegistry
{
    private static readonly JsonSerializerOptions JsonIndented = new() { WriteIndented = true };

    private void RegisterMiscFilters()
    {
        Register("AsBoolean", "Type", "Converts a value to true or false.",
            "{{ 'true' | AsBoolean }} → true",
            (input, _) => input switch
            {
                bool b => b,
                string s => s.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("t", StringComparison.OrdinalIgnoreCase)
                    || s == "1",
                decimal d => d != 0,
                _ => false,
            });

        Register("AsInteger", "Type", "Converts a value to a whole number.",
            "{{ '42.9' | AsInteger }} → 42",
            (input, _) => decimal.Truncate(Num(input, "AsInteger")));

        Register("AsDecimal", "Type", "Converts a value to a decimal number.",
            "{{ '3.14' | AsDecimal | Plus:1 }} → 4.14",
            (input, _) => Num(input, "AsDecimal"));

        Register("AsString", "Type", "Converts a value to a string.",
            "{{ 42 | AsString | Append:'!' }} → 42!",
            (input, _) => Str(input));

        Register("ToString", "Type", "Converts a value to a string (alias of AsString).",
            "{{ 42 | ToString | Size }} → 2",
            (input, _) => Str(input));

        Register("AsDateTime", "Type", "Converts a value to a date.",
            "{{ '2026-12-25' | AsDateTime | Date:'MMM d' }} → Dec 25",
            (input, _) => ToDate(input, "AsDateTime"));

        Register("ToJSON", "Type", "Serializes a value to indented JSON.",
            "{{ Person.Campus | ToJSON }}",
            (input, _) => JsonSerializer.Serialize(input, JsonIndented));

        Register("FromJSON", "Type", "Parses a JSON string into an object you can walk with dot notation.",
            "{% assign obj = jsonText | FromJSON %}{{ obj.Name }}",
            (input, _) => JsonObjectConverter.ToObject(JsonDocument.Parse(Str(input)).RootElement));

        Register("Debug", "Type", "Dumps a value's full structure as JSON inside a <pre> block (dev helper).",
            "{{ Person | Debug }}",
            (input, _) => "<pre>" + System.Net.WebUtility.HtmlEncode(JsonSerializer.Serialize(input, JsonIndented)) + "</pre>");
    }
}
