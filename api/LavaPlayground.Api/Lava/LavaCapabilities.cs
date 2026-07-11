namespace LavaPlayground.Api.Lava;

/// <summary>
/// The single source of truth for what the local engine supports, exposed
/// at GET /api/capabilities so the lint service (and anything else) can
/// learn it from the engine instead of maintaining its own copy.
/// </summary>
public static class LavaCapabilities
{
    public static readonly HashSet<string> EntityCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "person", "business", "group", "groupmember", "campus", "definedvalue", "contentchannelitem",
    };

    /// <summary>Block tags and their closers.</summary>
    public static readonly Dictionary<string, string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["if"] = "endif",
        ["unless"] = "endunless",
        ["for"] = "endfor",
        ["capture"] = "endcapture",
        ["comment"] = "endcomment",
        ["case"] = "endcase",
        ["raw"] = "endraw",
        ["tablerow"] = "endtablerow",
    };

    public static readonly string[] SimpleTags = { "assign", "cycle", "increment", "decrement" };

    public static readonly string[] BranchTags = { "elsif", "else", "when" };

    public static object Describe() => new
    {
        blockTags = BlockTags,
        simpleTags = SimpleTags,
        branchTags = BranchTags,
        entityCommands = EntityCommands.OrderBy(e => e),
        blockShortcodes = Shortcodes.BlockShortcodeNames.OrderBy(s => s),
        inlineShortcodes = Shortcodes.InlineShortcodeNames.OrderBy(s => s),
        whitespaceControl = true,
    };
}
