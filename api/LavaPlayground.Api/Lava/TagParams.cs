using System.Text.RegularExpressions;

namespace LavaPlayground.Api.Lava;

/// <summary>
/// Parses Rock-style tag/shortcode parameter lists:
///   where:'LastName == "Houston"' limit:'5' title:'My Panel'
/// Values are quoted with single or double quotes.
/// </summary>
public static partial class TagParams
{
    [GeneratedRegex(@"(\w+)\s*:\s*(?:'([^']*)'|""([^""]*)"")")]
    private static partial Regex ParamRegex();

    public static Dictionary<string, string> Parse(string markup)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ParamRegex().Matches(markup))
        {
            result[match.Groups[1].Value] = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
        }
        return result;
    }
}
