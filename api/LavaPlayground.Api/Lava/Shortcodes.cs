using System.Text;
using System.Text.RegularExpressions;

namespace LavaPlayground.Api.Lava;

/// <summary>
/// Built-in shortcodes, mirroring a useful slice of Rock's core set.
/// Block shortcodes take content up to {[ endname ]} and may contain
/// [[ item ... ]] ... [[ enditem ]] child blocks; inline ones are
/// self-contained.
/// </summary>
public static class Shortcodes
{
    public record Item(Dictionary<string, string> Params, string RawContent);

    private static readonly HashSet<string> BlockNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "accordion", "alert", "panel", "kpis",
    };

    private static readonly HashSet<string> InlineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "button", "youtube", "vimeo",
    };

    public static bool IsBlock(string name) => BlockNames.Contains(name);

    public static bool IsInline(string name) => InlineNames.Contains(name);

    public static IEnumerable<string> AllNames => BlockNames.Concat(InlineNames);

    public static IEnumerable<string> BlockShortcodeNames => BlockNames;

    public static IEnumerable<string> InlineShortcodeNames => InlineNames;

    /// <summary>
    /// Pulls [[ item ... ]] ... [[ enditem ]] child blocks out of a raw
    /// shortcode body, returning the remaining body and the items.
    /// </summary>
    public static (string Body, List<Item> Items) ExtractItems(string rawBody)
    {
        var items = new List<Item>();
        var body = new StringBuilder();
        var pos = 0;

        while (pos < rawBody.Length)
        {
            var open = rawBody.IndexOf("[[", pos, StringComparison.Ordinal);
            if (open == -1)
            {
                body.Append(rawBody[pos..]);
                break;
            }
            var close = rawBody.IndexOf("]]", open + 2, StringComparison.Ordinal);
            if (close == -1)
            {
                body.Append(rawBody[pos..]);
                break;
            }

            var header = rawBody[(open + 2)..close].Trim();
            if (!header.StartsWith("item", StringComparison.OrdinalIgnoreCase))
            {
                body.Append(rawBody[pos..(close + 2)]);
                pos = close + 2;
                continue;
            }

            var end = rawBody.IndexOf("[[ enditem ]]", close + 2, StringComparison.OrdinalIgnoreCase);
            if (end == -1)
            {
                end = rawBody.IndexOf("[[enditem]]", close + 2, StringComparison.OrdinalIgnoreCase);
            }
            if (end == -1)
            {
                throw new LavaException("Missing [[ enditem ]] inside shortcode.");
            }

            body.Append(rawBody[pos..open]);
            var contentStart = close + 2;
            items.Add(new Item(TagParams.Parse(header["item".Length..]), rawBody[contentStart..end]));
            pos = rawBody.IndexOf("]]", end, StringComparison.Ordinal) + 2;
        }

        return (body.ToString(), items);
    }

    public static string Render(string name, Dictionary<string, string> p, List<Item> items, string blockContent)
    {
        string Param(string key, string fallback = "") => p.TryGetValue(key, out var v) ? v : fallback;

        switch (name.ToLowerInvariant())
        {
            case "alert":
            {
                var type = Param("type", "info");
                return $"<div class=\"alert alert-{type}\">{blockContent}</div>";
            }

            case "panel":
            {
                var title = Param("title");
                var heading = title.Length > 0
                    ? $"<div class=\"panel-heading\"><h3 class=\"panel-title\">{title}</h3></div>"
                    : string.Empty;
                var footer = Param("footer");
                var footerHtml = footer.Length > 0 ? $"<div class=\"panel-footer\">{footer}</div>" : string.Empty;
                return $"<div class=\"panel panel-{Param("type", "default")}\">{heading}" +
                       $"<div class=\"panel-body\">{blockContent}</div>{footerHtml}</div>";
            }

            case "button":
            {
                var text = Param("text", "Click Here");
                var link = Param("link", "#");
                var type = Param("type", "primary");
                var size = Param("size");
                var sizeClass = size.Length > 0 ? $" btn-{size}" : string.Empty;
                return $"<a href=\"{link}\" class=\"btn btn-{type}{sizeClass}\">{text}</a>";
            }

            case "youtube":
            {
                var id = Param("id");
                if (id.Length == 0)
                {
                    throw new LavaException("The youtube shortcode requires an id parameter: {[ youtube id:'abc123' ]}.");
                }
                var width = Param("width", "100%");
                return "<div class=\"embed-responsive embed-responsive-16by9\">" +
                       $"<iframe width=\"{width}\" src=\"https://www.youtube.com/embed/{id}\" frameborder=\"0\" allowfullscreen></iframe></div>";
            }

            case "vimeo":
            {
                var id = Param("id");
                if (id.Length == 0)
                {
                    throw new LavaException("The vimeo shortcode requires an id parameter: {[ vimeo id:'123456' ]}.");
                }
                var width = Param("width", "100%");
                return "<div class=\"embed-responsive embed-responsive-16by9\">" +
                       $"<iframe width=\"{width}\" src=\"https://player.vimeo.com/video/{id}\" frameborder=\"0\" allowfullscreen></iframe></div>";
            }

            case "accordion":
            {
                var accordionId = "accordion-" + Math.Abs(string.Join('|', items.Select(i => i.RawContent)).GetHashCode() % 100000);
                var sb = new StringBuilder($"<div class=\"panel-group\" id=\"{accordionId}\">");
                var openFirst = !Param("openfirstitem", "true").Equals("false", StringComparison.OrdinalIgnoreCase);
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var title = item.Params.TryGetValue("title", out var t) ? t : $"Item {i + 1}";
                    var expanded = i == 0 && openFirst;
                    sb.Append("<div class=\"panel panel-default\">")
                      .Append($"<div class=\"panel-heading\"><h4 class=\"panel-title\"><a data-toggle=\"collapse\" data-parent=\"#{accordionId}\" href=\"#{accordionId}-{i}\">{title}</a></h4></div>")
                      .Append($"<div id=\"{accordionId}-{i}\" class=\"panel-collapse collapse{(expanded ? " in" : string.Empty)}\">")
                      .Append($"<div class=\"panel-body\">{item.RawContent}</div></div></div>");
                }
                sb.Append("</div>");
                return sb.ToString();
            }

            case "kpis":
            {
                var sb = new StringBuilder("<div class=\"kpi-container\" style=\"display:flex;gap:12px;flex-wrap:wrap\">");
                foreach (var item in items)
                {
                    string ItemParam(string key, string fallback = "") =>
                        item.Params.TryGetValue(key, out var v) ? v : fallback;
                    var color = ItemParam("color", "blue-500");
                    var icon = ItemParam("icon");
                    var iconHtml = icon.Length > 0 ? $"<i class=\"{icon}\"></i> " : string.Empty;
                    sb.Append($"<div class=\"kpi kpi-{color}\" style=\"flex:1;min-width:140px;border:1px solid #ddd;border-radius:8px;padding:12px\">")
                      .Append($"<div class=\"kpi-value\" style=\"font-size:1.6em;font-weight:700\">{iconHtml}{ItemParam("value", "--")}</div>")
                      .Append($"<div class=\"kpi-label\" style=\"color:#666\">{ItemParam("label")}</div>")
                      .Append("</div>");
                }
                sb.Append("</div>");
                return sb.ToString();
            }

            default:
                throw new LavaException($"Unknown shortcode {{[ {name} ]}}.");
        }
    }
}
