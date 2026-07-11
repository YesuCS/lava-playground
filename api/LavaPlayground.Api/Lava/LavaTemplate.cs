using System.Text;

namespace LavaPlayground.Api.Lava;

/// <summary>
/// A parsed Lava template. Parse once, render many times.
///
/// Supported syntax (a faithful subset of Rock Lava / Liquid):
///   {{ expression | Filter | Filter:'arg1',arg2 }}
///   {% assign name = expression %}
///   {% capture name %} ... {% endcapture %}
///   {% comment %} ... {% endcomment %}
///   {% if cond %} ... {% elsif cond %} ... {% else %} ... {% endif %}
///   {% unless cond %} ... {% endunless %}
///   {% for item in collection [reversed] [limit:n] %} ... {% endfor %}
///     (with the forloop object: index, index0, first, last, length)
/// </summary>
public class LavaTemplate
{
    private readonly List<Node> _nodes;

    private LavaTemplate(List<Node> nodes) => _nodes = nodes;

    public static LavaTemplate Parse(string source)
    {
        var parser = new Parser(source);
        var (nodes, terminator) = parser.ParseBlock(Array.Empty<string>());
        if (terminator != null)
        {
            throw new LavaException($"Unexpected {{% {terminator} %}} with no matching opening tag.");
        }
        return new LavaTemplate(nodes);
    }

    public string Render(RenderContext context)
    {
        var sb = new StringBuilder();
        foreach (var node in _nodes)
        {
            node.Render(context, sb);
        }
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Nodes
    // -----------------------------------------------------------------------

    private abstract class Node
    {
        public abstract void Render(RenderContext context, StringBuilder sb);
    }

    private class TextNode : Node
    {
        private string _text;
        public TextNode(string text) => _text = text;
        public void TrimEnd() => _text = _text.TrimEnd();
        public override void Render(RenderContext context, StringBuilder sb) => sb.Append(_text);
    }

    private class OutputNode : Node
    {
        private readonly FilteredExpression _expression;
        public OutputNode(FilteredExpression expression) => _expression = expression;

        public override void Render(RenderContext context, StringBuilder sb) =>
            sb.Append(LavaValue.ToDisplayString(_expression.Evaluate(context)));
    }

    private class AssignNode : Node
    {
        private readonly string _name;
        private readonly FilteredExpression _expression;

        public AssignNode(string name, FilteredExpression expression)
        {
            _name = name;
            _expression = expression;
        }

        public override void Render(RenderContext context, StringBuilder sb) =>
            context.SetGlobal(_name, _expression.Evaluate(context));
    }

    private class CaptureNode : Node
    {
        private readonly string _name;
        private readonly List<Node> _body;

        public CaptureNode(string name, List<Node> body)
        {
            _name = name;
            _body = body;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            var inner = new StringBuilder();
            foreach (var node in _body)
            {
                node.Render(context, inner);
            }
            context.SetGlobal(_name, inner.ToString());
        }
    }

    private class IfNode : Node
    {
        private readonly List<(Condition Condition, List<Node> Body)> _branches;
        private readonly List<Node>? _elseBody;

        public IfNode(List<(Condition, List<Node>)> branches, List<Node>? elseBody)
        {
            _branches = branches;
            _elseBody = elseBody;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            foreach (var (condition, body) in _branches)
            {
                if (condition.Evaluate(context))
                {
                    foreach (var node in body)
                    {
                        node.Render(context, sb);
                    }
                    return;
                }
            }
            if (_elseBody != null)
            {
                foreach (var node in _elseBody)
                {
                    node.Render(context, sb);
                }
            }
        }
    }

    private class ForNode : Node
    {
        private readonly string _variableName;
        private readonly FilteredExpression _collection;
        private readonly bool _reversed;
        private readonly int? _limit;
        private readonly List<Node> _body;

        public ForNode(string variableName, FilteredExpression collection, bool reversed, int? limit, List<Node> body)
        {
            _variableName = variableName;
            _collection = collection;
            _reversed = reversed;
            _limit = limit;
            _body = body;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            var value = _collection.Evaluate(context);
            List<object?> items;
            if (value is IDictionary<string, object?> dict)
            {
                // Iterating a dictionary (e.g. the result of GroupBy) yields
                // {Key, Value} entries, matching Rock's PropertyToKeyValue pattern.
                items = dict.Select(kv => (object?)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Key"] = kv.Key,
                    ["Value"] = kv.Value,
                }).ToList();
            }
            else if (value is IEnumerable<object?> rawItems && value is not string)
            {
                items = rawItems.ToList();
            }
            else
            {
                return; // Nothing iterable; render nothing, like Liquid does.
            }
            if (_reversed)
            {
                items.Reverse();
            }
            if (_limit.HasValue)
            {
                items = items.Take(_limit.Value).ToList();
            }

            context.PushScope();
            try
            {
                for (var i = 0; i < items.Count; i++)
                {
                    context.Set(_variableName, items[i]);
                    context.Set("forloop", new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["index"] = (decimal)(i + 1),
                        ["index0"] = (decimal)i,
                        ["first"] = i == 0,
                        ["last"] = i == items.Count - 1,
                        ["length"] = (decimal)items.Count,
                    });
                    foreach (var node in _body)
                    {
                        node.Render(context, sb);
                    }
                }
            }
            finally
            {
                context.PopScope();
            }
        }
    }

    // -----------------------------------------------------------------------
    // Parser: splits the source into text / {{ output }} / {% tag %} pieces
    // and builds the node tree with recursive descent on block tags.
    // -----------------------------------------------------------------------

    private class Parser
    {
        private readonly string _source;
        private int _pos;

        // Set by a -%} / -}} marker: the next text segment gets TrimStart'd.
        private bool _trimNextLeading;

        public Parser(string source) => _source = source;

        /// <summary>
        /// Extracts marker content between the given offsets, handling
        /// whitespace-control dashes ({{- -}} and {%- -%}).
        /// </summary>
        private (string Content, bool TrimBefore, bool TrimAfter) ExtractMarkerContent(int start, int end)
        {
            var raw = _source[start..end];
            var trimBefore = raw.StartsWith('-');
            var trimAfter = raw.Length > (trimBefore ? 1 : 0) && raw.EndsWith('-');
            if (trimBefore)
            {
                raw = raw[1..];
            }
            if (trimAfter)
            {
                raw = raw[..^1];
            }
            return (raw.Trim(), trimBefore, trimAfter);
        }

        private void AddTextBeforeMarker(List<Node> nodes, int markerStart, bool trimBefore)
        {
            var text = TakeText(_pos, markerStart);
            if (trimBefore)
            {
                text = text.TrimEnd();
                TrimLastText(nodes); // also handles markers separated only by earlier nodes
            }
            if (text.Length > 0)
            {
                nodes.Add(new TextNode(text));
            }
        }

        private string TakeText(int from, int to)
        {
            var text = _source[from..to];
            if (_trimNextLeading)
            {
                text = text.TrimStart();
                _trimNextLeading = false;
            }
            return text;
        }

        private static void TrimLastText(List<Node> nodes)
        {
            if (nodes.Count > 0 && nodes[^1] is TextNode text)
            {
                text.TrimEnd();
            }
        }

        /// <summary>
        /// Parses nodes until one of the terminator tags (e.g. "endif", "else")
        /// or the end of input. Returns the nodes and the full content of the
        /// terminator tag that stopped parsing (null at end of input).
        /// </summary>
        public (List<Node> Nodes, string? Terminator) ParseBlock(string[] terminators)
        {
            var nodes = new List<Node>();

            while (_pos < _source.Length)
            {
                var nextOutput = _source.IndexOf("{{", _pos, StringComparison.Ordinal);
                var nextTag = _source.IndexOf("{%", _pos, StringComparison.Ordinal);
                var nextShortcode = _source.IndexOf("{[", _pos, StringComparison.Ordinal);
                var candidates = new[] { nextOutput, nextTag, nextShortcode }.Where(i => i != -1).ToArray();
                var next = candidates.Length == 0 ? -1 : candidates.Min();

                if (next == -1)
                {
                    var tail = TakeText(_pos, _source.Length);
                    if (tail.Length > 0)
                    {
                        nodes.Add(new TextNode(tail));
                    }
                    _pos = _source.Length;
                    break;
                }

                if (next == nextShortcode)
                {
                    var text = TakeText(_pos, next);
                    if (text.Length > 0)
                    {
                        nodes.Add(new TextNode(text));
                    }
                    var close = _source.IndexOf("]}", next + 2, StringComparison.Ordinal);
                    if (close == -1)
                    {
                        throw new LavaException("Unclosed shortcode: found \"{[\" without a matching \"]}\".");
                    }
                    var content = _source[(next + 2)..close].Trim();
                    _pos = close + 2;
                    nodes.Add(ParseShortcode(content));
                }
                else if (next == nextOutput)
                {
                    var close = _source.IndexOf("}}", next + 2, StringComparison.Ordinal);
                    if (close == -1)
                    {
                        throw new LavaException("Unclosed output braces: found \"{{\" without a matching \"}}\".");
                    }
                    var (content, trimBefore, trimAfter) = ExtractMarkerContent(next + 2, close);
                    AddTextBeforeMarker(nodes, next, trimBefore);
                    _trimNextLeading = trimAfter;
                    if (content.Length == 0)
                    {
                        throw new LavaException("Empty output braces: \"{{ }}\" has nothing to render.");
                    }
                    nodes.Add(new OutputNode(FilteredExpression.Parse(content)));
                    _pos = close + 2;
                }
                else
                {
                    var close = _source.IndexOf("%}", next + 2, StringComparison.Ordinal);
                    if (close == -1)
                    {
                        throw new LavaException("Unclosed tag: found \"{%\" without a matching \"%}\".");
                    }
                    var (content, trimBefore, trimAfter) = ExtractMarkerContent(next + 2, close);
                    AddTextBeforeMarker(nodes, next, trimBefore);
                    _trimNextLeading = trimAfter;
                    _pos = close + 2;

                    var tagName = FirstWord(content);
                    if (terminators.Contains(tagName, StringComparer.OrdinalIgnoreCase))
                    {
                        return (nodes, content);
                    }

                    nodes.Add(ParseTag(tagName, content));
                }
            }

            return (nodes, null);
        }

        private Node ParseTag(string tagName, string content)
        {
            var rest = content[tagName.Length..].Trim();

            switch (tagName.ToLowerInvariant())
            {
                case "assign":
                {
                    var t = new ExpressionTokenizer(rest);
                    var name = t.Expect(TokenType.Identifier, "the variable name in {% assign %}").Text;
                    t.Expect(TokenType.Assign, "{% assign %} (expected \"=\")");
                    var expr = FilteredExpression.Parse(t);
                    return new AssignNode(name, expr);
                }

                case "capture":
                {
                    var name = rest;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        throw new LavaException("{% capture %} requires a variable name.");
                    }
                    var (body, term) = ParseBlock(new[] { "endcapture" });
                    if (term == null)
                    {
                        throw new LavaException("Missing {% endcapture %}.");
                    }
                    return new CaptureNode(name.Trim(), body);
                }

                case "comment":
                {
                    // Comment bodies are not parsed at all, matching Liquid.
                    ReadRawUntil("endcomment");
                    return new TextNode(string.Empty);
                }

                case "cycle":
                {
                    // {% cycle 'a', 'b' %} or {% cycle 'groupName': 'a', 'b' %}
                    var t = new ExpressionTokenizer(rest);
                    var first = Primary.Parse(t);
                    Primary? group = null;
                    var values = new List<Primary>();
                    if (t.TryConsume(TokenType.Colon))
                    {
                        group = first;
                        values.Add(Primary.Parse(t));
                    }
                    else
                    {
                        values.Add(first);
                    }
                    while (t.TryConsume(TokenType.Comma))
                    {
                        values.Add(Primary.Parse(t));
                    }
                    return new CycleNode(group, rest, values);
                }

                case "increment":
                case "decrement":
                {
                    var counter = rest.Trim();
                    if (counter.Length == 0)
                    {
                        throw new LavaException($"{{% {tagName} %}} requires a counter name.");
                    }
                    return new CounterNode(counter, increment: tagName.Equals("increment", StringComparison.OrdinalIgnoreCase));
                }

                case "tablerow":
                {
                    var t = new ExpressionTokenizer(rest);
                    var varName = t.Expect(TokenType.Identifier, "the loop variable in {% tablerow %}").Text;
                    if (!t.TryConsumeIdentifier("in"))
                    {
                        throw new LavaException("{% tablerow %} must look like: {% tablerow item in collection cols:n %}.");
                    }
                    var collection = FilteredExpression.Parse(t);
                    var cols = 1;
                    int? rowLimit = null;
                    while (t.Peek().Type == TokenType.Identifier)
                    {
                        var modifier = t.Next().Text;
                        t.Expect(TokenType.Colon, $"{modifier}:n in {{% tablerow %}}");
                        var number = int.Parse(t.Expect(TokenType.Number, $"{modifier}:n").Text);
                        if (modifier.Equals("cols", StringComparison.OrdinalIgnoreCase)) cols = Math.Max(1, number);
                        else if (modifier.Equals("limit", StringComparison.OrdinalIgnoreCase)) rowLimit = number;
                        else throw new LavaException($"Unknown {{% tablerow %}} modifier \"{modifier}\".");
                    }
                    var (body, term) = ParseBlock(new[] { "endtablerow" });
                    if (term == null)
                    {
                        throw new LavaException("Missing {% endtablerow %}.");
                    }
                    return new TableRowNode(varName, collection, cols, rowLimit, body);
                }

                case "if":
                case "unless":
                {
                    var isUnless = tagName.Equals("unless", StringComparison.OrdinalIgnoreCase);
                    var endTag = isUnless ? "endunless" : "endif";
                    var condition = Condition.Parse(rest);
                    if (isUnless)
                    {
                        condition = new NotCondition(condition);
                    }

                    var branches = new List<(Condition, List<Node>)>();
                    List<Node>? elseBody = null;

                    var (body, term) = ParseBlock(new[] { "elsif", "else", endTag });
                    branches.Add((condition, body));

                    while (term != null && FirstWord(term).Equals("elsif", StringComparison.OrdinalIgnoreCase))
                    {
                        var elsifCondition = Condition.Parse(term["elsif".Length..].Trim());
                        (body, term) = ParseBlock(new[] { "elsif", "else", endTag });
                        branches.Add((elsifCondition, body));
                    }

                    if (term != null && FirstWord(term).Equals("else", StringComparison.OrdinalIgnoreCase))
                    {
                        (elseBody, term) = ParseBlock(new[] { endTag });
                    }

                    if (term == null)
                    {
                        throw new LavaException($"Missing {{% {endTag} %}}.");
                    }
                    return new IfNode(branches, elseBody);
                }

                case "raw":
                {
                    return new TextNode(ReadRawUntil("endraw"));
                }

                case "case":
                {
                    var subject = FilteredExpression.Parse(rest);
                    var branches = new List<(List<FilteredExpression>, List<Node>)>();
                    List<Node>? elseBody = null;

                    // Text between {% case %} and the first {% when %} is discarded.
                    var (_, term) = ParseBlock(new[] { "when", "else", "endcase" });

                    while (term != null && FirstWord(term).Equals("when", StringComparison.OrdinalIgnoreCase))
                    {
                        var values = ParseWhenValues(term["when".Length..].Trim());
                        var (body, next) = ParseBlock(new[] { "when", "else", "endcase" });
                        branches.Add((values, body));
                        term = next;
                    }

                    if (term != null && FirstWord(term).Equals("else", StringComparison.OrdinalIgnoreCase))
                    {
                        (elseBody, term) = ParseBlock(new[] { "endcase" });
                    }

                    if (term == null)
                    {
                        throw new LavaException("Missing {% endcase %}.");
                    }
                    return new CaseNode(subject, branches, elseBody);
                }

                case "for":
                {
                    var t = new ExpressionTokenizer(rest);
                    var varName = t.Expect(TokenType.Identifier, "the loop variable in {% for %}").Text;
                    if (!t.TryConsumeIdentifier("in"))
                    {
                        throw new LavaException("{% for %} must look like: {% for item in collection %}.");
                    }
                    var collection = FilteredExpression.Parse(t);

                    var reversed = false;
                    int? limit = null;
                    while (t.Peek().Type == TokenType.Identifier)
                    {
                        var modifier = t.Next().Text;
                        if (modifier.Equals("reversed", StringComparison.OrdinalIgnoreCase))
                        {
                            reversed = true;
                        }
                        else if (modifier.Equals("limit", StringComparison.OrdinalIgnoreCase))
                        {
                            t.Expect(TokenType.Colon, "limit:n in {% for %}");
                            limit = int.Parse(t.Expect(TokenType.Number, "limit:n in {% for %}").Text);
                        }
                        else
                        {
                            throw new LavaException($"Unknown {{% for %}} modifier \"{modifier}\".");
                        }
                    }

                    var (body, term) = ParseBlock(new[] { "endfor" });
                    if (term == null)
                    {
                        throw new LavaException("Missing {% endfor %}.");
                    }
                    return new ForNode(varName, collection, reversed, limit, body);
                }

                default:
                    if (EntityCommandNode.SupportedEntities.Contains(tagName))
                    {
                        var parameters = TagParams.Parse(rest);
                        var (body, term) = ParseBlock(new[] { "end" + tagName.ToLowerInvariant() });
                        if (term == null)
                        {
                            throw new LavaException($"Missing {{% end{tagName.ToLowerInvariant()} %}}.");
                        }
                        return new EntityCommandNode(tagName, parameters, body);
                    }
                    if (tagName.StartsWith("end", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("else", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("elsif", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("when", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new LavaException($"Unexpected {{% {tagName} %}} with no matching opening tag.");
                    }
                    throw new LavaException(
                        $"Unknown tag {{% {tagName} %}}. Supported: assign, capture, comment, if, unless, for, case, raw, " +
                        "cycle, increment, decrement, tablerow, and the entity commands " +
                        string.Join(", ", EntityCommandNode.SupportedEntities.OrderBy(e => e)) + ".");
            }
        }

        private static List<FilteredExpression> ParseWhenValues(string content)
        {
            // {% when 'a' %}, {% when 'a' or 'b' %}, {% when 'a', 'b' %}
            var values = new List<FilteredExpression>();
            var t = new ExpressionTokenizer(content);
            values.Add(FilteredExpression.Parse(t));
            while (t.TryConsume(TokenType.Comma) || t.TryConsumeIdentifier("or"))
            {
                values.Add(FilteredExpression.Parse(t));
            }
            if (t.Peek().Type != TokenType.End)
            {
                throw new LavaException($"Unexpected \"{t.Peek().Text}\" in {{% when %}}.");
            }
            return values;
        }

        /// <summary>Reads literal text until {% endTag %}, without parsing the contents.</summary>
        private string ReadRawUntil(string endTag)
        {
            var start = _pos;
            var search = _pos;
            while (true)
            {
                var open = _source.IndexOf("{%", search, StringComparison.Ordinal);
                if (open == -1)
                {
                    throw new LavaException($"Missing {{% {endTag} %}}.");
                }
                var close = _source.IndexOf("%}", open + 2, StringComparison.Ordinal);
                if (close == -1)
                {
                    throw new LavaException($"Missing {{% {endTag} %}}.");
                }
                if (_source[(open + 2)..close].Trim().Equals(endTag, StringComparison.OrdinalIgnoreCase))
                {
                    _pos = close + 2;
                    return _source[start..open];
                }
                search = open + 2;
            }
        }

        /// <summary>
        /// Parses a {[ shortcode ]} marker. Block shortcodes capture their raw
        /// body up to {[ endname ]}; the body is rendered as Lava at render time.
        /// </summary>
        private Node ParseShortcode(string content)
        {
            if (content.Length == 0)
            {
                throw new LavaException("Empty shortcode: \"{[ ]}\" has no name.");
            }

            var name = FirstWord(content);
            if (name.StartsWith("end", StringComparison.OrdinalIgnoreCase))
            {
                throw new LavaException($"Unexpected {{[ {name} ]}} with no matching opening shortcode.");
            }

            var parameters = TagParams.Parse(content[name.Length..].Trim());

            if (Shortcodes.IsInline(name))
            {
                return new ShortcodeNode(name, parameters, rawBody: null);
            }
            if (!Shortcodes.IsBlock(name))
            {
                throw new LavaException(
                    $"Unknown shortcode {{[ {name} ]}}. Available: {string.Join(", ", Shortcodes.AllNames.OrderBy(n => n))}.");
            }

            // Read the raw body until the matching {[ endname ]}, honoring nesting.
            var endName = "end" + name.ToLowerInvariant();
            var depth = 1;
            var start = _pos;
            var search = _pos;
            while (true)
            {
                var open = _source.IndexOf("{[", search, StringComparison.Ordinal);
                if (open == -1)
                {
                    throw new LavaException($"Missing {{[ {endName} ]}}.");
                }
                var close = _source.IndexOf("]}", open + 2, StringComparison.Ordinal);
                if (close == -1)
                {
                    throw new LavaException($"Missing {{[ {endName} ]}}.");
                }
                var inner = _source[(open + 2)..close].Trim();
                var innerName = FirstWord(inner);
                if (innerName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    depth++;
                }
                else if (innerName.Equals(endName, StringComparison.OrdinalIgnoreCase))
                {
                    depth--;
                    if (depth == 0)
                    {
                        _pos = close + 2;
                        return new ShortcodeNode(name, parameters, _source[start..open]);
                    }
                }
                search = close + 2;
            }
        }

        private static string FirstWord(string content)
        {
            var end = 0;
            while (end < content.Length && !char.IsWhiteSpace(content[end]))
            {
                end++;
            }
            return content[..end];
        }
    }

    private class CycleNode : Node
    {
        private readonly Primary? _group;
        private readonly string _fallbackKey;
        private readonly List<Primary> _values;

        public CycleNode(Primary? group, string fallbackKey, List<Primary> values)
        {
            _group = group;
            _fallbackKey = fallbackKey;
            _values = values;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            var key = _group != null ? LavaValue.ToDisplayString(_group.Evaluate(context)) : _fallbackKey;
            context.CycleState.TryGetValue(key, out var index);
            context.CycleState[key] = index + 1;
            sb.Append(LavaValue.ToDisplayString(_values[index % _values.Count].Evaluate(context)));
        }
    }

    private class CounterNode : Node
    {
        private readonly string _name;
        private readonly bool _increment;

        public CounterNode(string name, bool increment)
        {
            _name = name;
            _increment = increment;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            context.Counters.TryGetValue(_name, out var value);
            if (_increment)
            {
                // Liquid semantics: output current value, then increment (first output is 0).
                sb.Append(value);
                context.Counters[_name] = value + 1;
            }
            else
            {
                // Decrement first, then output (first output is -1).
                context.Counters[_name] = value - 1;
                sb.Append(value - 1);
            }
        }
    }

    private class TableRowNode : Node
    {
        private readonly string _variableName;
        private readonly FilteredExpression _collection;
        private readonly int _cols;
        private readonly int? _limit;
        private readonly List<Node> _body;

        public TableRowNode(string variableName, FilteredExpression collection, int cols, int? limit, List<Node> body)
        {
            _variableName = variableName;
            _collection = collection;
            _cols = cols;
            _limit = limit;
            _body = body;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            var value = _collection.Evaluate(context);
            if (value is not IEnumerable<object?> rawItems || value is string)
            {
                return;
            }
            var items = rawItems.ToList();
            if (_limit.HasValue)
            {
                items = items.Take(_limit.Value).ToList();
            }

            context.PushScope();
            try
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var row = i / _cols + 1;
                    var col = i % _cols + 1;
                    if (col == 1)
                    {
                        sb.Append($"<tr class=\"row{row}\">");
                    }
                    context.Set(_variableName, items[i]);
                    sb.Append($"<td class=\"col{col}\">");
                    foreach (var node in _body)
                    {
                        node.Render(context, sb);
                    }
                    sb.Append("</td>");
                    if (col == _cols || i == items.Count - 1)
                    {
                        sb.Append("</tr>");
                    }
                }
            }
            finally
            {
                context.PopScope();
            }
        }
    }

    private class EntityCommandNode : Node
    {
        public static HashSet<string> SupportedEntities => LavaCapabilities.EntityCommands;

        private readonly string _entity;
        private readonly Dictionary<string, string> _params;
        private readonly List<Node> _body;

        public EntityCommandNode(string entity, Dictionary<string, string> parameters, List<Node> body)
        {
            _entity = entity.ToLowerInvariant();
            _params = parameters;
            _body = body;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            if (!context.EntityData.TryGetValue(_entity, out var dataset))
            {
                throw new LavaException(
                    $"No sample data for {{% {_entity} %}}. Available locally: " +
                    string.Join(", ", context.EntityData.Keys.OrderBy(k => k)) +
                    ". (On a real Rock server this queries the database.)");
            }

            IEnumerable<object?> results = dataset;

            if (TryParam("id", out var id))
            {
                results = dataset.Where(i => LavaValue.LooseEquals(PathValue(i, "Id"), id));
            }
            if (TryParam("where", out var where))
            {
                var predicate = WhereClause.Compile(RenderParam(where, context));
                results = results.Where(predicate);
            }
            if (TryParam("sort", out var sort))
            {
                var parts = sort.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
                var comparer = Comparer<object?>.Create(LavaValue.CompareNumeric);
                results = descending
                    ? results.OrderByDescending(i => PathValue(i, parts[0]), comparer)
                    : results.OrderBy(i => PathValue(i, parts[0]), comparer);
            }
            if (TryParam("offset", out var offset))
            {
                results = results.Skip(int.Parse(offset));
            }
            if (TryParam("limit", out var limit))
            {
                results = results.Take(int.Parse(limit));
            }

            var list = results.ToList();
            var variableName = TryParam("iterator", out var iterator) ? iterator : _entity;

            context.PushScope();
            try
            {
                // With id: the variable is the single entity; otherwise it's the result set.
                context.Set(variableName, _params.ContainsKey("id") ? list.FirstOrDefault() : list);
                foreach (var node in _body)
                {
                    node.Render(context, sb);
                }
            }
            finally
            {
                context.PopScope();
            }
        }

        private bool TryParam(string name, out string value)
        {
            if (_params.TryGetValue(name, out var v))
            {
                value = v;
                return true;
            }
            value = string.Empty;
            return false;
        }

        private static string RenderParam(string value, RenderContext context) =>
            value.Contains("{{") || value.Contains("{%")
                ? Parse(value).Render(context)
                : value;

        private static object? PathValue(object? item, string path)
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
    }

    private class ShortcodeNode : Node
    {
        private readonly string _name;
        private readonly Dictionary<string, string> _params;
        private readonly string? _rawBody;

        public ShortcodeNode(string name, Dictionary<string, string> parameters, string? rawBody)
        {
            _name = name.ToLowerInvariant();
            _params = parameters;
            _rawBody = rawBody;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            // Parameter values may themselves contain Lava.
            var resolved = _params.ToDictionary(
                kv => kv.Key,
                kv => RenderLava(kv.Value, context),
                StringComparer.OrdinalIgnoreCase);

            var items = new List<Shortcodes.Item>();
            var blockContent = string.Empty;
            if (_rawBody != null)
            {
                var (body, parsedItems) = Shortcodes.ExtractItems(_rawBody);
                items = parsedItems
                    .Select(i => new Shortcodes.Item(
                        i.Params.ToDictionary(kv => kv.Key, kv => RenderLava(kv.Value, context), StringComparer.OrdinalIgnoreCase),
                        RenderLava(i.RawContent, context).Trim()))
                    .ToList();
                blockContent = RenderLava(body, context).Trim();
            }

            sb.Append(Shortcodes.Render(_name, resolved, items, blockContent));
        }

        private static string RenderLava(string source, RenderContext context) =>
            source.Contains("{{") || source.Contains("{%") ? Parse(source).Render(context) : source;
    }

    private class CaseNode : Node
    {
        private readonly FilteredExpression _subject;
        private readonly List<(List<FilteredExpression> Values, List<Node> Body)> _branches;
        private readonly List<Node>? _elseBody;

        public CaseNode(FilteredExpression subject, List<(List<FilteredExpression>, List<Node>)> branches, List<Node>? elseBody)
        {
            _subject = subject;
            _branches = branches;
            _elseBody = elseBody;
        }

        public override void Render(RenderContext context, StringBuilder sb)
        {
            var subject = _subject.Evaluate(context);
            foreach (var (values, body) in _branches)
            {
                if (values.Any(v => LavaValue.LooseEquals(subject, v.Evaluate(context))))
                {
                    foreach (var node in body)
                    {
                        node.Render(context, sb);
                    }
                    return;
                }
            }
            if (_elseBody != null)
            {
                foreach (var node in _elseBody)
                {
                    node.Render(context, sb);
                }
            }
        }
    }

    private class NotCondition : Condition
    {
        private readonly Condition _inner;
        public NotCondition(Condition inner) => _inner = inner;
        public override bool Evaluate(RenderContext context) => !_inner.Evaluate(context);
    }
}
