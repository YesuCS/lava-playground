"""Known Lava filters and tags.

LOCAL_FILTERS mirrors the C# filter registry
(api/LavaPlayground.Api/Lava/LavaFilters.*.cs). The lint service also
refreshes this list from the render API's GET /api/filters at startup
(see main.py), so the static list is only a fallback.

REMOTE_ONLY_* are real Rock Lava filters/tags that require a live Rock
database (entities, attributes, page context). The linter recognizes them
so it can say "connect to a Rock server" instead of "unknown".
"""

LOCAL_FILTERS = {
    # Text
    "Upcase", "Downcase", "Capitalize", "TitleCase", "SentenceCase",
    "Humanize", "Truncate", "TruncateWords", "Replace", "ReplaceFirst",
    "ReplaceLast", "Remove", "RemoveFirst", "Append", "Prepend", "Split",
    "Strip", "Trim", "StripHtml", "StripNewlines", "NewlineToBr", "Right",
    "Left", "Slice", "Pluralize", "Singularize", "PluralizeForQuantity",
    "ToQuantity", "Possessive", "ToCssClass", "HtmlEncode", "HtmlDecode",
    "Escape", "EscapeOnce", "EscapeDataString", "UrlEncode", "UrlDecode",
    "ObfuscateEmail", "Linkify", "FromMarkdown", "ReadTime", "RegExMatch",
    "RegExMatchValue", "RegExMatchValues", "RegExReplace", "Default",
    # Numeric
    "Plus", "Minus", "Times", "DividedBy", "Modulo", "Abs", "Floor",
    "Ceiling", "Round", "AtLeast", "AtMost", "Format", "FormatAsCurrency",
    "NumberToOrdinal", "NumberToOrdinalWords", "NumberToWords",
    "NumberToRomanNumerals", "RandomNumber",
    # Date
    "Date", "DateAdd", "DateDiff", "DaysFromNow", "DaysSince", "DaysUntil",
    "DaysInMonth", "HumanizeDateTime", "HumanizeTimeSpan", "SundayDate",
    "ToMidnight",
    # Collection
    "Size", "First", "Last", "Index", "Take", "Skip", "Join", "Select",
    "Map", "Sort", "Reverse", "Shuffle", "Uniq", "Distinct", "Compact",
    "Concat", "Contains", "Sum", "Where", "GroupBy", "PropertyToKeyValue",
    "AddToArray", "RemoveFromArray", "AddToDictionary",
    "AllKeysFromDictionary",
    # Type coercion
    "AsBoolean", "AsInteger", "AsDecimal", "AsString", "ToString",
    "AsDateTime", "ToJSON", "FromJSON", "Debug",
    # Color
    "Lighten", "Darken", "Saturate", "Desaturate", "AdjustHue", "Grayscale",
    "FadeIn", "FadeOut", "Mix", "Tint", "Shade",
}

# Real Rock filters that need a live Rock instance (entity/page context).
REMOTE_ONLY_FILTERS = {
    # Person
    "Address", "Campus", "Spouse", "Children", "Parents", "PhoneNumber",
    "FamilySalutation", "Group", "Groups", "GroupsAttended",
    "GeofencingGroups", "NearestGroup", "HasSignedDocument", "Steps",
    "GetUserPreference", "SetUserPreference", "DeleteUserPreference",
    "PersonById", "PersonByGuid", "PersonByAliasGuid",
    "PersonByPersonAliasId", "PersonActionIdentifier",
    "PersonImpersonationToken", "ZebraPhoto", "Notes",
    # Attribute
    "Attribute", "AttributeValue",
    # Utility / page / head
    "PageParameter", "PageRoute", "ResolveRockUrl", "PersistedDataset",
    "AppendFollowing", "AppendSegments", "AppendWatches", "AddToMergeFields",
    "ReadCookie", "WriteCookie", "AddResponseHeader", "AddCssLink",
    "AddScriptLink", "AddLinkTagToHead", "AddMetaTagToHead",
    # Security / misc
    "Encrypt", "Decrypt", "AsEnum", "DatesFromICal",
    "DateRangeFromSlidingFormat", "AsDateTimeUtc", "IdHash", "FromIdHash",
}

KNOWN_FILTERS = set(LOCAL_FILTERS)  # refreshed live in main.py

# Block tags and their required closers (supported by the local engine).
BLOCK_TAGS = {
    "if": "endif",
    "unless": "endunless",
    "for": "endfor",
    "capture": "endcapture",
    "comment": "endcomment",
    "case": "endcase",
    "raw": "endraw",
}

END_TAGS = {closer: opener for opener, closer in BLOCK_TAGS.items()}

# Tags only valid inside an {% if %} / {% case %} block.
BRANCH_TAGS = {"elsif", "else", "when"}

# Simple (non-block) tags.
SIMPLE_TAGS = {"assign"}

# Rock-only block tags: entity commands and server-side commands. These
# render only on a real Rock server (remote mode) with the command
# enabled on the endpoint.
REMOTE_ONLY_BLOCK_TAGS = {
    "person", "business", "group", "groupmember", "campus", "definedvalue",
    "contentchannelitem", "entity", "sql", "execute", "webrequest",
    "workflowactivate", "cache", "javascript", "stylesheet",
    "interactionwrite", "interactioncontentchannelitemwrite",
}

ALL_TAGS = set(BLOCK_TAGS) | set(END_TAGS) | BRANCH_TAGS | SIMPLE_TAGS
