namespace Nightshift.Core;

public interface ILintRule { IEnumerable<Diagnostic> Apply(Graph graph); }
public static class Validation
{
    public static IReadOnlyList<Diagnostic> Validate(Graph graph, HandlerRegistry? registry = null, IEnumerable<ILintRule>? extraRules = null)
    {
        var result = new List<Diagnostic>();
        void Add(string rule, Severity severity, string message, Node? n = null, Edge? e = null) => result.Add(new(rule, severity, message, n?.Id, e == null ? null : $"{e.From}->{e.To}", n?.Location ?? e?.Location));
        var starts = graph.Nodes.Values.Where(n => n.IsStart).ToList(); var exits = graph.Nodes.Values.Where(n => n.IsExit).ToList();
        if (starts.Count != 1) Add("start_node", Severity.Error, $"Expected one start; found {starts.Count}");
        if (exits.Count != 1) Add("terminal_node", Severity.Error, $"Expected one exit; found {exits.Count}");
        foreach (var edge in graph.Edges)
        {
            if (!graph.Nodes.ContainsKey(edge.To) || !graph.Nodes.ContainsKey(edge.From)) Add("edge_target_exists", Severity.Error, "Edge endpoint does not exist", e: edge);
            try { Conditions.Parse(edge.Condition); } catch (FormatException ex) { Add("condition_syntax", Severity.Error, ex.Message, e: edge); }
            Check(edge.Attributes, null, edge);
            if (edge.Attributes.Bool("loop_restart")) Add("unsupported_feature", Severity.Error, "loop_restart is deferred; use bounded graph revisits", e: edge);
        }
        foreach (var start in starts) if (graph.Edges.Any(e => e.To == start.Id)) Add("start_no_incoming", Severity.Error, "Start has incoming edges", start);
        foreach (var exit in exits) if (graph.Outgoing(exit.Id).Any()) Add("exit_no_outgoing", Severity.Error, "Exit has outgoing edges", exit);
        if (starts.Count == 1)
        {
            var seen = new HashSet<string>(); var pending = new Stack<string>(); pending.Push(starts[0].Id);
            while (pending.TryPop(out var id)) if (seen.Add(id)) foreach (var e in graph.Outgoing(id)) pending.Push(e.To);
            foreach (var n in graph.Nodes.Values.Where(n => !seen.Contains(n.Id))) Add("reachability", Severity.Error, "Node unreachable from start", n);
        }
        foreach (var node in graph.Nodes.Values)
        {
            Check(node.Attributes, node);
            bool known = registry?.Contains(node.Type) ?? HandlerRegistry.BuiltInTypes.Contains(node.Type);
            if (!known) { Add("type_known", Severity.Warning, $"Unrecognized type '{node.Type}'", node); Add("unsupported_handler", Severity.Error, $"No handler for '{node.Type}'; no codergen fallback", node); }
            if (node.Type == "codergen" && node.Attributes.Text("prompt").Length == 0 && node.Attributes.Text("label").Length == 0) Add("prompt_on_llm_nodes", Severity.Warning, "Codergen has no prompt or explicit label", node);
            if (node.Attributes.Bool("goal_gate") && Routing.Recovery(node, graph, true) == null) Add("goal_gate_has_retry", Severity.Warning, "Goal gate has no valid recovery target", node);
            if (node.Type == "tool" && node.Attributes.Text("tool_command").Length == 0) Add("tool_command", Severity.Error, "Tool requires tool_command", node);
            if (node.Type == "wait.human" && !graph.Outgoing(node.Id).Any()) Add("human_choices", Severity.Error, "Human gate requires choices", node);
        }
        Check(graph.Attributes);
        foreach (var rule in extraRules ?? []) result.AddRange(rule.Apply(graph));
        return result;

        void Check(Attributes a, Node? node = null, Edge? edge = null)
        {
            foreach (var (key, value) in a)
            {
                string text = value.Text;
                if (key is "fidelity" or "default_fidelity" && text.Length > 0 && !Handoff.Modes.Contains(text))
                { Add("fidelity_valid", Severity.Warning, $"Unknown fidelity '{text}'", node, edge); Add("unsupported_fidelity", Severity.Error, $"Cannot execute fidelity '{text}'", node, edge); }
                if (key is "retry_target" or "fallback_retry_target" && text.Length > 0 && !graph.Nodes.ContainsKey(text)) Add("retry_target_exists", Severity.Warning, $"Unknown target '{text}'; fallback resolution will skip it", node, edge);
                if (key is "max_retries" or "default_max_retries" or "default_max_retry" or "weight")
                {
                    if (!int.TryParse(text, out int v) || (key != "weight" && (v < 0 || v > 1000))) Add("attribute_type", Severity.Error, $"{key} must be an integer" + (key == "weight" ? "" : " in 0..1000"), node, edge);
                }
                if (key is "goal_gate" or "auto_status" or "allow_partial" or "loop_restart" && text is not "true" and not "false") Add("attribute_type", Severity.Error, $"{key} must be true or false", node, edge);
                if (key is "timeout" or "human.timeout") { try { Durations.Parse(text); } catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException) { Add("attribute_type", Severity.Error, ex.Message, node, edge); } }
                if (key == "reasoning_effort" && text is not "low" and not "medium" and not "high") Add("attribute_type", Severity.Error, "reasoning_effort must be low, medium or high", node, edge);
                if (key is "tool_hooks.pre" or "tool_hooks.post" && text.Length > 0) Add("unsupported_feature", Severity.Error, "Per-tool hooks require backend instrumentation; unsupported", node, edge);
                if (key == "auto_status" && text == "true") Add("auto_status", Severity.Warning, "Auto-status is synthetic success, unsuitable as an authoritative verification verdict", node, edge);
            }
        }
    }
}
