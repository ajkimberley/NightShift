using System.Text.RegularExpressions;

namespace Nightshift.Core;

public sealed record StyleRule(string Selector, Attributes Declarations, int Specificity, int Order);
public static class Stylesheet
{
    public static readonly string[] Properties = ["llm_model", "llm_provider", "reasoning_effort"];
    public static IReadOnlyList<StyleRule> Parse(string source)
    {
        var rules = new List<StyleRule>(); int i = 0;
        void Space() { while (i < source.Length && char.IsWhiteSpace(source[i])) i++; }
        while (true)
        {
            Space(); if (i == source.Length) break;
            int start = i; while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] != '{') i++;
            string selector = source[start..i]; Space();
            if (!Regex.IsMatch(selector, @"^(\*|#[A-Za-z_][A-Za-z0-9_]*|\.[a-z0-9-]+|[A-Za-z_][A-Za-z0-9_]*)$")) throw new FormatException($"Invalid stylesheet selector '{selector}'");
            if (i == source.Length || source[i++] != '{') throw new FormatException("Expected stylesheet '{'");
            var values = new Attributes();
            while (true)
            {
                Space(); if (i >= source.Length) throw new FormatException("Unclosed stylesheet rule");
                if (source[i] == '}') { i++; break; }
                start = i; while (i < source.Length && source[i] != ':') i++;
                if (i == source.Length) throw new FormatException("Missing declaration ':'");
                string property = source[start..i].Trim(); i++; Space();
                if (!Properties.Contains(property)) throw new FormatException($"Unknown stylesheet property '{property}'");
                start = i;
                if (i < source.Length && source[i] == '"')
                {
                    i++; bool closed = false;
                    while (i < source.Length) { if (source[i] == '\\') { i += 2; continue; } if (source[i++] == '"') { closed = true; break; } }
                    if (!closed) throw new FormatException("Unclosed stylesheet string");
                }
                else while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] is not ';' and not '}') i++;
                var ts = new Lexer(source[start..Math.Min(i, source.Length)]).Scan();
                if (ts.Count != 2 || (!ts[0].Quoted && !Regex.IsMatch(ts[0].Text, @"^[A-Za-z_][A-Za-z0-9_.:-]*$"))) throw new FormatException("Invalid stylesheet value");
                values[property] = new(ts[0].Text, ValueKind.String, ts[0].Location);
                Space(); if (i < source.Length && source[i] == ';') i++;
                else if (i >= source.Length || source[i] != '}') throw new FormatException("Expected declaration ';' or '}'");
            }
            if (values.Count == 0) throw new FormatException("Empty stylesheet rule");
            rules.Add(new(selector, values, selector[0] switch { '*' => 0, '.' => 2, '#' => 3, _ => 1 }, rules.Count));
        }
        return rules;
    }
    public static bool Matches(StyleRule rule, Node node) => rule.Selector[0] switch
    {
        '*' => true, '#' => node.Id == rule.Selector[1..], '.' => node.Classes.Contains(rule.Selector[1..]), _ => node.Shape == rule.Selector
    };
}

public interface IGraphTransform { Graph Apply(Graph graph); }
public sealed record PreparedPipeline(Graph Graph, IReadOnlyList<Diagnostic> Diagnostics);
public static class Preparation
{
    public static PreparedPipeline Prepare(string dot, HandlerRegistry? registry = null, IEnumerable<IGraphTransform>? transforms = null)
    {
        Graph graph = new DotParser().Parse(dot);
        var diagnostics = new List<Diagnostic>();
        try
        {
            var rules = Stylesheet.Parse(graph.Attributes.Text("model_stylesheet"));
            foreach (var node in graph.Nodes.Values)
            {
                node.Classes.UnionWith(node.Attributes.Text("class").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                foreach (string property in Stylesheet.Properties)
                {
                    if (node.ExplicitKeys.Contains(property)) { node.ConfigurationSources[property] = $"explicit DOT {node.Attributes[property].Location}"; continue; }
                    var selected = rules.Where(r => Stylesheet.Matches(r, node) && r.Declarations.ContainsKey(property)).OrderBy(r => r.Specificity).ThenBy(r => r.Order).LastOrDefault();
                    if (selected != null) { node.Attributes[property] = selected.Declarations[property]; node.ConfigurationSources[property] = $"stylesheet {selected.Selector} rule {selected.Order + 1}"; }
                    else if (node.Attributes.ContainsKey(property)) node.ConfigurationSources[property] = $"node default {node.Attributes[property].Location}";
                    else if (graph.Attributes.TryGetValue(property, out var v)) { node.Attributes[property] = v; node.ConfigurationSources[property] = "graph default"; }
                }
                if (!node.Attributes.ContainsKey("reasoning_effort")) { node.Attributes.Set("reasoning_effort", "high"); node.ConfigurationSources["reasoning_effort"] = "system default"; }
                if (node.Attributes.TryGetValue("prompt", out var prompt)) node.Attributes["prompt"] = prompt with { Text = prompt.Text.Replace("$goal", graph.Attributes.Text("goal"), StringComparison.Ordinal) };
            }
        }
        catch (FormatException e) { diagnostics.Add(new("stylesheet_syntax", Severity.Error, e.Message)); }
        foreach (var transform in transforms ?? []) graph = transform.Apply(graph);
        diagnostics.AddRange(Validation.Validate(graph, registry));
        return new(graph, diagnostics);
    }
    public static void RequireValid(PreparedPipeline pipeline)
    {
        if (pipeline.Diagnostics.Any(d => d.Severity == Severity.Error)) throw new PipelineException(string.Join("\n", pipeline.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => $"{d.Rule}: {d.Message}")));
    }
}

public sealed record HandoffConfiguration(string Fidelity, string FidelitySource, string ThreadKey, string ThreadSource);
public static class Handoff
{
    public static readonly string[] Modes = ["full", "truncate", "compact", "summary:low", "summary:medium", "summary:high"];
    public static HandoffConfiguration Resolve(Graph graph, Node node, Edge? incoming, string previous)
    {
        var fidelity = new[] { (incoming?.Attributes.Text("fidelity") ?? "", "edge"), (node.Attributes.Text("fidelity"), "node"), (graph.Attributes.Text("default_fidelity"), "graph"), ("compact", "runtime default") }.First(p => p.Item1.Length > 0);
        var thread = new[] { (node.Attributes.Text("thread_id"), "node"), (incoming?.Attributes.Text("thread_id") ?? "", "edge"), (graph.Attributes.Text("thread_id"), "graph"), (node.SubgraphThread, "subgraph"), (previous, "previous node") }.FirstOrDefault(p => p.Item1.Length > 0);
        return new(fidelity.Item1, fidelity.Item2, thread.Item1 ?? "", thread.Item2 ?? "none");
    }
}
