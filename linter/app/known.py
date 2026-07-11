"""Known Lava filters and tags.

Keep KNOWN_FILTERS in sync with the C# filter registry
(api/LavaPlayground.Api/Lava/LavaFilters.cs). The API also exposes the
live list at GET /api/filters if you ever want to diff them.
"""

KNOWN_FILTERS = {
    # String
    "Upcase", "Downcase", "Capitalize", "TitleCase", "SentenceCase",
    "Humanize", "Truncate", "Replace", "Remove", "Append", "Prepend",
    "Split", "Trim", "Right", "Left", "Slice", "Pluralize",
    "PluralizeForQuantity", "Possessive", "ToCssClass", "HtmlEncode",
    # Number
    "Plus", "Minus", "Times", "DividedBy", "Modulo", "Floor", "Ceiling",
    "Format", "NumberToOrdinal", "NumberToOrdinalWords", "NumberToWords",
    "NumberToRomanNumerals", "AsInteger", "AsDecimal",
    # Date
    "Date", "DaysUntil", "HumanizeTimeSpan",
    # Array
    "Size", "First", "Last", "Join", "Map", "Sort", "Reverse", "Uniq", "Where",
    # Logic
    "Default",
}

# Block tags and their required closers.
BLOCK_TAGS = {
    "if": "endif",
    "unless": "endunless",
    "for": "endfor",
    "capture": "endcapture",
    "comment": "endcomment",
}

END_TAGS = {closer: opener for opener, closer in BLOCK_TAGS.items()}

# Tags only valid inside an {% if %} / {% unless %} block.
BRANCH_TAGS = {"elsif", "else"}

# Simple (non-block) tags.
SIMPLE_TAGS = {"assign"}

ALL_TAGS = set(BLOCK_TAGS) | set(END_TAGS) | BRANCH_TAGS | SIMPLE_TAGS
