using System.Text.RegularExpressions;

namespace Nightshift.Core;

public sealed record StageRequest(Node Node, Graph Graph, RunContext Context, string Directory, int Visit, int Attempt, HandoffConfiguration Handoff, Action<RunEvent> Emit);
public interface IHandler { Task<Outcome> ExecuteAsync(StageRequest request, CancellationToken cancellationToken); }
public sealed class DelegateHandler(Func<StageRequest, CancellationToken, Task<Outcome>> action) : IHandler
{
    public Task<Outcome> ExecuteAsync(StageRequest request, CancellationToken cancellationToken) => action(request, cancellationToken);
}
public sealed class HandlerRegistry
{
    public static readonly string[] BuiltInTypes = ["start", "exit", "codergen", "conditional", "tool", "wait.human"];
    private readonly Dictionary<string, IHandler> handlers = new(StringComparer.Ordinal);
    public bool Contains(string type) => handlers.ContainsKey(type);
    public void Register(string type, IHandler handler) => handlers[type] = handler;
    public IHandler Resolve(Node node) => handlers.TryGetValue(node.Type, out var h) ? h : throw new PipelineException($"Unsupported handler '{node.Type}'");
    public static HandlerRegistry Create(ICodergenBackend backend, IInterviewer interviewer, string workingDirectory)
    {
        var registry = new HandlerRegistry();
        foreach (string type in new[] { "start", "exit", "conditional" }) registry.Register(type, new DelegateHandler((_, _) => Task.FromResult(Outcome.Success())));
        registry.Register("codergen", new CodergenHandler(backend)); registry.Register("tool", new ToolHandler(workingDirectory)); registry.Register("wait.human", new HumanHandler(interviewer)); return registry;
    }
}
public sealed record BackendCapabilities(string Name, IReadOnlyList<string> FidelityModes, bool SessionReuse = false, bool PerToolHooks = false, string Notes = "");
public sealed record BackendResult(string Response, Outcome? Outcome);
public interface ICodergenBackend
{
    BackendCapabilities Capabilities { get; }
    Task<BackendResult> RunAsync(StageRequest request, string prompt, CancellationToken cancellationToken);
}
public sealed class ScriptedBackend : ICodergenBackend
{
    public BackendCapabilities Capabilities { get; } = new("scripted", Handoff.Modes, Notes: "Deterministic simulation only; no model fidelity or correctness claim");
    public Task<BackendResult> RunAsync(StageRequest r, string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = r.Node.Attributes.Text("script.outcomes", "success").Split(',', StringSplitOptions.TrimEntries);
        int count = int.TryParse(r.Context.Get($"script.calls.{r.Node.Id}"), out var c) ? c : 0;
        count += r.Attempt - 1;
        string status = sequence[Math.Min(count, sequence.Length - 1)];
        StageStatus parsed = status switch { "success" => StageStatus.Success, "fail" => StageStatus.Fail, "retry" => StageStatus.Retry, "partial_success" => StageStatus.PartialSuccess, "skipped" => StageStatus.Skipped, "transient" => throw new TransientException("Scripted transient failure"), _ => throw new PipelineException($"Invalid scripted outcome '{status}'") };
        var updates = new RunContext(); updates.Set($"script.calls.{r.Node.Id}", count + 1); updates.Set("last_stage", r.Node.Id);
        foreach (var (key, value) in r.Node.Attributes.Where(p => p.Key.StartsWith("script.set.", StringComparison.Ordinal))) updates.Set(key[11..], value.Text);
        return Task.FromResult(new BackendResult($"[SCRIPTED; no task correctness claim] {r.Node.Id}: {prompt}", new Outcome { Status = parsed, ContextUpdates = updates.Snapshot(), FailureReason = parsed is StageStatus.Fail or StageStatus.Retry ? "Scripted failure" : "", Notes = "Deterministic simulation" }));
    }
}
public sealed class CodergenHandler(ICodergenBackend backend) : IHandler
{
    public async Task<Outcome> ExecuteAsync(StageRequest r, CancellationToken cancellationToken)
    {
        if (!backend.Capabilities.FidelityModes.Contains(r.Handoff.Fidelity)) return Outcome.Fail($"Backend '{backend.Capabilities.Name}' does not implement fidelity '{r.Handoff.Fidelity}'");
        string prompt = r.Node.Attributes.Text("prompt", r.Node.Label); if (prompt.Length == 0) prompt = r.Node.Label;
        await File.WriteAllTextAsync(Path.Combine(r.Directory, "prompt.md"), prompt, cancellationToken);
        await AtomicFile.WriteJsonAsync(Path.Combine(r.Directory, "configuration.json"), new { r.Handoff, Backend = backend.Capabilities, Model = r.Node.Attributes.Text("llm_model"), Provider = r.Node.Attributes.Text("llm_provider"), ReasoningEffort = r.Node.Attributes.Text("reasoning_effort"), Sources = r.Node.ConfigurationSources }, cancellationToken);
        var result = await backend.RunAsync(r, prompt, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(r.Directory, "response.md"), result.Response, cancellationToken);
        if (result.Outcome != null) return result.Outcome;
        if (r.Node.Attributes.Bool("auto_status")) return Outcome.Success("auto-status: no status supplied; not verified correctness");
        return Outcome.Fail("Backend returned text without an explicit outcome; task correctness is unknown");
    }
}
public sealed class ToolHandler(string workingDirectory) : IHandler
{
    public async Task<Outcome> ExecuteAsync(StageRequest r, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", r.Node.Attributes.Text("tool_command")], workingDirectory, "", r.Directory, cancellationToken);
        if (result.ExitCode != 0) return Outcome.Fail($"Command exited {result.ExitCode}; see stderr.txt");
        var updates = new RunContext(); updates.Set("tool.output", result.Stdout); updates.Set("tool.exit_code", result.ExitCode);
        return new() { Status = StageStatus.Success, ContextUpdates = updates.Snapshot(), Notes = "Command exited zero; domain correctness requires explicit checks" };
    }
}
public sealed class HumanHandler(IInterviewer interviewer) : IHandler
{
    public async Task<Outcome> ExecuteAsync(StageRequest r, CancellationToken cancellationToken)
    {
        var options = r.Graph.Outgoing(r.Node.Id).Select(e =>
        {
            string label = e.Attributes.Text("label", e.To); if (label.Length == 0) label = e.To;
            var m = Regex.Match(label.Trim(), @"^(?:\[([A-Za-z0-9])\]|([A-Za-z0-9])\)|([A-Za-z0-9])\s+-\s+)");
            string key = m.Success ? m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value : label[..1];
            return new Option(key, label, e.To);
        }).ToList();
        if (options.Count == 0) return Outcome.Fail("No human choices");
        if (options.Select(o => o.Key.ToUpperInvariant()).Distinct().Count() != options.Count) return Outcome.Fail("Ambiguous human accelerator keys");
        var question = new Question(r.Node.Label, QuestionType.MultipleChoice, options, r.Node.Id,
            r.Node.Attributes.ContainsKey("human.timeout") ? Durations.Parse(r.Node.Attributes.Text("human.timeout")) : null, r.Node.Attributes.Text("human.default_choice"));
        r.Emit(new(RunEventKind.InterviewStarted, r.Node.Id, r.Visit, r.Attempt, "Question presented"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (question.Timeout != null) timeout.CancelAfter(question.Timeout.Value);
        Answer answer;
        try { answer = await interviewer.AskAsync(question, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { answer = new("TIMEOUT"); }
        await AtomicFile.WriteJsonAsync(Path.Combine(r.Directory, "interview.json"), new InterviewRecord(question, answer, DateTimeOffset.UtcNow), cancellationToken);
        r.Emit(new(answer.Value == "TIMEOUT" ? RunEventKind.InterviewTimeout : RunEventKind.InterviewCompleted, r.Node.Id, r.Visit, r.Attempt, answer.Value));
        if (answer.Value == "SKIPPED") return Outcome.Fail("Human skipped interaction");
        string value = answer.Value;
        if (value == "TIMEOUT")
        {
            if (string.IsNullOrEmpty(question.Default)) return new() { Status = StageStatus.Retry, FailureReason = "Human timeout without default" };
            value = question.Default;
        }
        var selected = options.Where(o => string.Equals(o.Key, value, StringComparison.OrdinalIgnoreCase) || o.Label == value || o.Target == value).ToList();
        if (selected.Count != 1) return Outcome.Fail($"Invalid or ambiguous human answer '{value}'");
        var updates = new RunContext(); updates.Set("human.gate.selected", selected[0].Key); updates.Set("human.gate.label", selected[0].Label); updates.Set("human.gate.identity", answer.Identity);
        return new() { Status = StageStatus.Success, SuggestedNextIds = [selected[0].Target], ContextUpdates = updates.Snapshot(), Notes = "Choice recorded; identity is not authenticated" };
    }
}
