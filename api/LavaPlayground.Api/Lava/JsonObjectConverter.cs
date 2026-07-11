using System.Text.Json;

namespace LavaPlayground.Api.Lava;

/// <summary>
/// Converts a System.Text.Json document into a plain object tree
/// (Dictionary / List / string / decimal / bool / null) that the
/// Lava engine can walk with case-insensitive property access.
/// </summary>
public static class JsonObjectConverter
{
    public static Dictionary<string, object?> ToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new LavaException("The merge context must be a JSON object.");
        }

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ToObject(prop.Value);
        }
        return dict;
    }

    public static object? ToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ToDictionary(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ToObject).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
