using System.Text.RegularExpressions;

namespace Nightshift.Core;

public sealed record ConditionClause(string Key, bool NotEqual, string Value);
public static class Conditions
{
    // Anchored scanner permits && inside quoted literals without splitting the string.
    private static readonly Regex Clause = new("\\G\\s*(outcome|preferred_label|context\\.[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*)\\s*(!=|=)\\s*(\"(?:[^\"\\\\]|\\\\[nt\"\\\\])*\"|-?[0-9]+|[A-Za-z_][A-Za-z0-9_.:-]*)\\s*", RegexOptions.CultureInvariant);
    public static IReadOnlyList<ConditionClause> Parse(string expression)
    {
        var result = new List<ConditionClause>(); int offset = 0;
        if (string.IsNullOrWhiteSpace(expression)) return result;
        while (offset < expression.Length)
        {
            var m = Clause.Match(expression, offset);
            if (!m.Success) throw new FormatException($"Invalid condition at offset {offset}: {expression}");
            string literal = m.Groups[3].Value;
            if (literal.StartsWith('"')) literal = new Lexer(literal).Scan()[0].Text;
            result.Add(new(m.Groups[1].Value, m.Groups[2].Value == "!=", literal)); offset = m.Index + m.Length;
            if (offset == expression.Length) break;
            if (!expression.AsSpan(offset).StartsWith("&&")) throw new FormatException("Only =, != and && are supported");
            offset += 2;
            if (string.IsNullOrWhiteSpace(expression[offset..])) throw new FormatException("Missing clause after &&");
        }
        return result;
    }
    public static bool Evaluate(string expression, Outcome outcome, RunContext context) => Parse(expression).All(c =>
    {
        string actual = c.Key switch
        {
            "outcome" => StatusText(outcome.Status), "preferred_label" => outcome.PreferredLabel,
            _ => context.Has(c.Key) ? context.Get(c.Key) : context.Get(c.Key[8..])
        };
        return c.NotEqual ? actual != c.Value : actual == c.Value;
    });
    public static string StatusText(StageStatus status) => status switch { StageStatus.PartialSuccess => "partial_success", _ => status.ToString().ToLowerInvariant() };
}

public static class Routing
{
    public static string NormalizeLabel(string label) => Regex.Replace(label.Trim().ToLowerInvariant(), @"^(\[[a-z0-9]\]\s*|[a-z0-9]\)\s*|[a-z0-9]\s+-\s*)", "");
    public static Edge? Best(IEnumerable<Edge> edges) => edges.OrderByDescending(e => e.Attributes.Int("weight")).ThenBy(e => e.To, StringComparer.Ordinal).FirstOrDefault();
    public static Edge? Select(IEnumerable<Edge> source, Outcome outcome, RunContext context)
    {
        var edges = source.ToList();
        var conditional = Best(edges.Where(e => e.Condition.Length > 0 && Conditions.Evaluate(e.Condition, outcome, context)));
        if (conditional != null) return conditional;
        var plain = edges.Where(e => e.Condition.Length == 0).ToList();
        if (outcome.PreferredLabel.Length > 0)
        {
            var match = plain.FirstOrDefault(e => NormalizeLabel(e.Attributes.Text("label")) == NormalizeLabel(outcome.PreferredLabel));
            if (match != null) return match;
        }
        foreach (string id in outcome.SuggestedNextIds) { var e = plain.FirstOrDefault(e => e.To == id); if (e != null) return e; }
        return Best(plain);
    }
    public static Edge? Failure(IEnumerable<Edge> edges, Outcome outcome, RunContext context) => Best(edges.Where(e =>
        e.Condition.Length > 0 && Conditions.Parse(e.Condition).Any(c => c is { Key: "outcome", NotEqual: false, Value: "fail" }) && Conditions.Evaluate(e.Condition, outcome, context)));
    public static string? Recovery(Node node, Graph graph, bool includeGraph) => new[]
    {
        node.Attributes.Text("retry_target"), node.Attributes.Text("fallback_retry_target"),
        includeGraph ? graph.Attributes.Text("retry_target") : "", includeGraph ? graph.Attributes.Text("fallback_retry_target") : ""
    }.FirstOrDefault(graph.Nodes.ContainsKey);
}
