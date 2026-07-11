using System.Globalization;
using System.Text.RegularExpressions;

namespace LavaPlayground.Api.Lava;

public partial class LavaFilterRegistry
{
    private void RegisterStringFilters()
    {
        Register("Upcase", "Text", "Converts a string to uppercase.",
            "{{ 'hello' | Upcase }} → HELLO",
            (input, _) => Str(input).ToUpperInvariant());

        Register("Downcase", "Text", "Converts a string to lowercase.",
            "{{ 'HELLO' | Downcase }} → hello",
            (input, _) => Str(input).ToLowerInvariant());

        Register("Capitalize", "Text", "Capitalizes the first character of a string.",
            "{{ 'houston' | Capitalize }} → Houston",
            (input, _) =>
            {
                var s = Str(input);
                return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
            });

        Register("TitleCase", "Text", "Capitalizes the first letter of each word.",
            "{{ 'the loop campus' | TitleCase }} → The Loop Campus",
            (input, _) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Str(input).ToLowerInvariant()));

        Register("SentenceCase", "Text", "Capitalizes only the first letter of the sentence.",
            "{{ 'WELCOME HOME' | SentenceCase }} → Welcome home",
            (input, _) =>
            {
                var s = Str(input).ToLowerInvariant();
                return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
            });

        Register("Humanize", "Text", "Turns camelCase or snake_case identifiers into readable text.",
            "{{ 'FirstTimeGuest' | Humanize }} → First time guest",
            (input, _) =>
            {
                var s = Str(input);
                s = Regex.Replace(s, "([a-z0-9])([A-Z])", "$1 $2");
                s = s.Replace('_', ' ').Replace('-', ' ');
                s = Regex.Replace(s, @"\s+", " ").Trim().ToLowerInvariant();
                return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
            });

        Register("Truncate", "Text", "Shortens a string to n characters, appending an ellipsis if truncated.",
            "{{ 'Hallelujah' | Truncate:4 }} → Hall…",
            (input, args) =>
            {
                var s = Str(input);
                var length = (int)Num(Arg(args, 0) ?? 50m, "Truncate");
                return s.Length <= length ? s : s[..length] + "…";
            });

        Register("TruncateWords", "Text", "Shortens a string to n words.",
            "{{ 'the quick brown fox jumps' | TruncateWords:3 }} → the quick brown…",
            (input, args) =>
            {
                var count = (int)Num(Arg(args, 0) ?? 15m, "TruncateWords");
                var words = Str(input).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return words.Length <= count ? Str(input) : string.Join(' ', words.Take(count)) + "…";
            });

        Register("Replace", "Text", "Replaces every occurrence of a value in the string.",
            "{{ 'pew pew' | Replace:'pew','wow' }} → wow wow",
            (input, args) => Str(input).Replace(Str(Arg(args, 0)), Str(Arg(args, 1))));

        Register("ReplaceFirst", "Text", "Replaces only the first occurrence of a value.",
            "{{ 'pew pew' | ReplaceFirst:'pew','wow' }} → wow pew",
            (input, args) => ReplaceOccurrence(Str(input), Str(Arg(args, 0)), Str(Arg(args, 1)), first: true));

        Register("ReplaceLast", "Text", "Replaces only the last occurrence of a value.",
            "{{ 'pew pew' | ReplaceLast:'pew','wow' }} → pew wow",
            (input, args) => ReplaceOccurrence(Str(input), Str(Arg(args, 0)), Str(Arg(args, 1)), first: false));

        Register("Remove", "Text", "Removes every occurrence of a value from the string.",
            "{{ 'Lava is hot hot' | Remove:' hot' }} → Lava is",
            (input, args) => Str(input).Replace(Str(Arg(args, 0)), string.Empty));

        Register("RemoveFirst", "Text", "Removes only the first occurrence of a value.",
            "{{ 'a-b-c' | RemoveFirst:'-' }} → ab-c",
            (input, args) => ReplaceOccurrence(Str(input), Str(Arg(args, 0)), string.Empty, first: true));

        Register("Append", "Text", "Appends a value to the end of a string.",
            "{{ 'Houston' | Append:' First' }} → Houston First",
            (input, args) => Str(input) + Str(Arg(args, 0)));

        Register("Prepend", "Text", "Prepends a value to the start of a string.",
            "{{ 'First' | Prepend:'Houston ' }} → Houston First",
            (input, args) => Str(Arg(args, 0)) + Str(input));

        Register("Split", "Text", "Splits a string into an array on a separator.",
            "{{ 'a,b,c' | Split:',' | Size }} → 3",
            (input, args) => Str(input).Split(Str(Arg(args, 0))).Cast<object?>().ToList());

        Register("Strip", "Text", "Removes leading and trailing whitespace.",
            "{{ '  hi  ' | Strip }} → hi",
            (input, _) => Str(input).Trim());

        Register("Trim", "Text", "Removes leading and trailing whitespace (alias of Strip).",
            "{{ '  hi  ' | Trim }} → hi",
            (input, _) => Str(input).Trim());

        Register("StripHtml", "Text", "Removes HTML tags from a string.",
            "{{ '<b>Big</b> news' | StripHtml }} → Big news",
            (input, _) => Regex.Replace(Str(input), "<[^>]*>", string.Empty));

        Register("StripNewlines", "Text", "Removes all newline characters.",
            "{{ multiLineText | StripNewlines }}",
            (input, _) => Str(input).Replace("\r", string.Empty).Replace("\n", string.Empty));

        Register("NewlineToBr", "Text", "Converts newlines to HTML <br/> tags.",
            "{{ multiLineText | NewlineToBr }}",
            (input, _) => Str(input).Replace("\r\n", "<br/>").Replace("\n", "<br/>"));

        Register("Right", "Text", "Returns the rightmost n characters.",
            "{{ 'Houston' | Right:3 }} → ton",
            (input, args) =>
            {
                var s = Str(input);
                var n = (int)Num(Arg(args, 0) ?? 1m, "Right");
                return n >= s.Length ? s : s[^n..];
            });

        Register("Left", "Text", "Returns the leftmost n characters.",
            "{{ 'Houston' | Left:3 }} → Hou",
            (input, args) =>
            {
                var s = Str(input);
                var n = (int)Num(Arg(args, 0) ?? 1m, "Left");
                return n >= s.Length ? s : s[..n];
            });

        Register("Slice", "Text", "Returns a substring starting at an index (negative counts from the end).",
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

        Register("Pluralize", "Text", "Returns the plural form of a singular English word.",
            "{{ 'person' | Pluralize }} → people",
            (input, _) => Pluralize(Str(input)));

        Register("Singularize", "Text", "Returns the singular form of a plural English word.",
            "{{ 'ministries' | Singularize }} → ministry",
            (input, _) => Singularize(Str(input)));

        Register("PluralizeForQuantity", "Text", "Pluralizes the word only when the quantity is not 1.",
            "{{ 'kid' | PluralizeForQuantity:3 }} → kids",
            (input, args) =>
            {
                var qty = Num(Arg(args, 0) ?? 1m, "PluralizeForQuantity");
                return qty == 1m ? Str(input) : Pluralize(Str(input));
            });

        Register("ToQuantity", "Text", "Prefixes the count and pluralizes the word when needed.",
            "{{ 'phone' | ToQuantity:3 }} → 3 phones",
            (input, args) =>
            {
                var qty = Num(Arg(args, 0) ?? 1m, "ToQuantity");
                var word = qty == 1m ? Str(input) : Pluralize(Str(input));
                return $"{LavaValue.ToDisplayString(qty)} {word}";
            });

        Register("Possessive", "Text", "Makes a name possessive, following English style rules.",
            "{{ 'Yesu' | Possessive }} → Yesu's",
            (input, _) =>
            {
                var s = Str(input);
                return s.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? s + "'" : s + "'s";
            });

        Register("ToCssClass", "Text", "Lowercases a string and replaces non-alphanumerics with hyphens.",
            "{{ 'The Loop Campus' | ToCssClass }} → the-loop-campus",
            (input, _) => Regex.Replace(Str(input).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'));

        Register("HtmlEncode", "Text", "Encodes a string so it is safe to place in HTML.",
            "{{ '<b>hi</b>' | HtmlEncode }} → &lt;b&gt;hi&lt;/b&gt;",
            (input, _) => System.Net.WebUtility.HtmlEncode(Str(input)));

        Register("HtmlDecode", "Text", "Decodes HTML entities back to characters.",
            "{{ '&lt;b&gt;' | HtmlDecode }} → <b>",
            (input, _) => System.Net.WebUtility.HtmlDecode(Str(input)));

        Register("Escape", "Text", "Escapes HTML special characters.",
            "{{ '<b>hi</b>' | Escape }} → &lt;b&gt;hi&lt;/b&gt;",
            (input, _) => System.Net.WebUtility.HtmlEncode(Str(input)));

        Register("EscapeOnce", "Text", "Escapes HTML without double-escaping entities that are already escaped.",
            "{{ '&lt;b&gt; & <i>' | EscapeOnce }} → &lt;b&gt; &amp; &lt;i&gt;",
            (input, _) =>
            {
                var unescaped = System.Net.WebUtility.HtmlDecode(Str(input));
                return System.Net.WebUtility.HtmlEncode(unescaped);
            });

        Register("EscapeDataString", "Text", "URL-encodes a string for use in a query string.",
            "{{ 'the loop & more' | EscapeDataString }} → the%20loop%20%26%20more",
            (input, _) => Uri.EscapeDataString(Str(input)));

        Register("UrlEncode", "Text", "URL-encodes a string (alias of EscapeDataString).",
            "{{ 'a b' | UrlEncode }} → a%20b",
            (input, _) => Uri.EscapeDataString(Str(input)));

        Register("UrlDecode", "Text", "Decodes a URL-encoded string.",
            "{{ 'a%20b' | UrlDecode }} → a b",
            (input, _) => Uri.UnescapeDataString(Str(input)));

        Register("ObfuscateEmail", "Text", "Masks an email address for public display.",
            "{{ 'sam@example.com' | ObfuscateEmail }} → sxxxxx@example.com",
            (input, _) =>
            {
                var s = Str(input);
                var at = s.IndexOf('@');
                return at <= 0 ? s : s[0] + "xxxxx" + s[at..];
            });

        Register("Linkify", "Text", "Turns URLs in the text into HTML anchor tags.",
            "{{ 'see https://rockrms.com' | Linkify }}",
            (input, _) => Regex.Replace(
                Str(input),
                @"(https?://[^\s<]+)",
                "<a href=\"$1\">$1</a>"));

        Register("FromMarkdown", "Text", "Converts basic Markdown (headers, bold, italic, links, code) to HTML.",
            "{{ '**Big** news' | FromMarkdown }} → <p><strong>Big</strong> news</p>",
            (input, _) => MarkdownToHtml(Str(input)));

        Register("ReadTime", "Text", "Estimates reading time at 275 words per minute.",
            "{{ longText | ReadTime }} → 3 mins",
            (input, _) =>
            {
                var words = Str(input).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                var mins = (int)Math.Ceiling(words / 275.0);
                return mins <= 1 ? "1 min" : $"{mins} mins";
            });

        Register("RegExMatch", "Text", "Returns true when the string matches a regular expression.",
            "{{ '555-1234' | RegExMatch:'\\d{3}-\\d{4}' }} → true",
            (input, args) => Regex.IsMatch(Str(input), Str(Arg(args, 0))));

        Register("RegExMatchValue", "Text", "Returns the first regular-expression match in the string.",
            "{{ 'Room 214 is open' | RegExMatchValue:'\\d+' }} → 214",
            (input, args) =>
            {
                var match = Regex.Match(Str(input), Str(Arg(args, 0)));
                return match.Success ? match.Value : null;
            });

        Register("RegExMatchValues", "Text", "Returns all regular-expression matches as an array.",
            "{{ 'a1 b2 c3' | RegExMatchValues:'\\d' | Join:',' }} → 1,2,3",
            (input, args) => Regex.Matches(Str(input), Str(Arg(args, 0)))
                .Select(m => (object?)m.Value)
                .ToList());

        Register("RegExReplace", "Text", "Replaces regular-expression matches with a value.",
            "{{ 'a1b2' | RegExReplace:'\\d','-' }} → a-b-",
            (input, args) => Regex.Replace(Str(input), Str(Arg(args, 0)), Str(Arg(args, 1))));

        Register("WithFallback", "Logic", "Appends/prepends text when the input has a value; otherwise returns the fallback.",
            "{{ Person.NickName | WithFallback:'Hi, ',' friend','prepend' }}",
            (input, args) =>
            {
                var successText = Str(Arg(args, 0));
                var fallbackText = Str(Arg(args, 1));
                var order = Str(Arg(args, 2) ?? "prepend");
                var s = Str(input);
                if (s.Length == 0)
                {
                    return fallbackText;
                }
                return order.Equals("append", StringComparison.OrdinalIgnoreCase) ? s + successText : successText + s;
            });

        Register("Default", "Logic", "Returns a fallback when the input is null, false, or an empty string.",
            "{{ Person.MiddleName | Default:'(none)' }}",
            (input, args) => input == null || input as bool? == false || (input is string s2 && s2.Length == 0)
                ? Arg(args, 0)
                : input);
    }

    private static string ReplaceOccurrence(string source, string find, string replacement, bool first)
    {
        if (find.Length == 0)
        {
            return source;
        }
        var index = first
            ? source.IndexOf(find, StringComparison.Ordinal)
            : source.LastIndexOf(find, StringComparison.Ordinal);
        return index == -1
            ? source
            : source[..index] + replacement + source[(index + find.Length)..];
    }

    /// <summary>A deliberately small Markdown subset: headers, bold, italic, inline code, links, lists.</summary>
    private static string MarkdownToHtml(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var html = new System.Text.StringBuilder();
        var inList = false;
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                html.Append("<p>").Append(Inline(string.Join(" ", paragraph))).Append("</p>\n");
                paragraph.Clear();
            }
        }

        void CloseList()
        {
            if (inList)
            {
                html.Append("</ul>\n");
                inList = false;
            }
        }

        static string Inline(string text)
        {
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            text = Regex.Replace(text, @"\*(.+?)\*", "<em>$1</em>");
            text = Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");
            text = Regex.Replace(text, @"\[(.+?)\]\((.+?)\)", "<a href=\"$2\">$1</a>");
            return text;
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var headerMatch = Regex.Match(trimmed, @"^(#{1,6})\s+(.*)$");
            if (headerMatch.Success)
            {
                FlushParagraph();
                CloseList();
                var level = headerMatch.Groups[1].Value.Length;
                html.Append($"<h{level}>").Append(Inline(headerMatch.Groups[2].Value)).Append($"</h{level}>\n");
            }
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                FlushParagraph();
                if (!inList)
                {
                    html.Append("<ul>\n");
                    inList = true;
                }
                html.Append("<li>").Append(Inline(trimmed[2..])).Append("</li>\n");
            }
            else if (trimmed.Length == 0)
            {
                FlushParagraph();
                CloseList();
            }
            else
            {
                CloseList();
                paragraph.Add(trimmed);
            }
        }
        FlushParagraph();
        CloseList();
        return html.ToString().TrimEnd('\n');
    }
}
