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
        private readonly string _text;
        public TextNode(string text) => _text = text;
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

        public Parser(string source) => _source = source;

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
                var next = (nextOutput, nextTag) switch
                {
                    (-1, -1) => -1,
                    (-1, _) => nextTag,
                    (_, -1) => nextOutput,
                    _ => Math.Min(nextOutput, nextTag),
                };

                if (next == -1)
                {
                    nodes.Add(new TextNode(_source[_pos..]));
                    _pos = _source.Length;
                    break;
                }

                if (next > _pos)
                {
                    nodes.Add(new TextNode(_source[_pos..next]));
                }

                if (next == nextOutput)
                {
                    var close = _source.IndexOf("}}", next + 2, StringComparison.Ordinal);
                    if (close == -1)
                    {
                        throw new LavaException("Unclosed output braces: found \"{{\" without a matching \"}}\".");
                    }
                    var content = _source[(next + 2)..close].Trim();
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
                    var content = _source[(next + 2)..close].Trim();
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
                    var (_, term) = ParseBlock(new[] { "endcomment" });
                    if (term == null)
                    {
                        throw new LavaException("Missing {% endcomment %}.");
                    }
                    return new TextNode(string.Empty);
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
                    return new TextNode(ReadRawUntilEndRaw());
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
                    if (tagName.StartsWith("end", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("else", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("elsif", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("when", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new LavaException($"Unexpected {{% {tagName} %}} with no matching opening tag.");
                    }
                    throw new LavaException($"Unknown tag {{% {tagName} %}}. Supported tags: assign, capture, comment, if, unless, for, case, raw.");
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

        private string ReadRawUntilEndRaw()
        {
            var start = _pos;
            var search = _pos;
            while (true)
            {
                var open = _source.IndexOf("{%", search, StringComparison.Ordinal);
                if (open == -1)
                {
                    throw new LavaException("Missing {% endraw %}.");
                }
                var close = _source.IndexOf("%}", open + 2, StringComparison.Ordinal);
                if (close == -1)
                {
                    throw new LavaException("Missing {% endraw %}.");
                }
                if (_source[(open + 2)..close].Trim().Equals("endraw", StringComparison.OrdinalIgnoreCase))
                {
                    _pos = close + 2;
                    return _source[start..open];
                }
                search = open + 2;
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
