using System.Collections.Immutable;
using System.Text.Json;
using Nightshift.Core;

namespace Nightshift.Adapters;

public abstract class ProcessAgent(IWorkerLauncher launcher, string executable, string version) : ICodingAgent
{
    public abstract AgentCapabilities Capabilities { get; }
    protected string Executable { get; } = executable;
    protected string Version { get; } = version;
    public const string ClaimSchema = """{"type":"object","properties":{"outcome":{"type":"string","enum":["success","partial_success","fail","retry"]},"notes":{"type":"string"}},"required":["outcome","notes"],"additionalProperties":false}""";
    public abstract WorkerCommand BuildCommand(AgentTask task);
    protected abstract AgentResult Translate(WorkerResult result);
    public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken token)
    {
        if (task.Fidelity != "truncate" || task.SessionRequested || task.SteeringRequested || task.HooksRequested)
            return new(AgentResultKind.ConfigurationError, "", Detail: "Only fresh truncate tasks are supported; sessions, steering and hooks are unsupported");
        try
        {
            var result = await launcher.ExecuteAsync(BuildCommand(task), token);
            if (result.ExitCode != 0) return new(AgentResultKind.WorkerFailure, result.Stdout, Detail: "Agent exited " + result.ExitCode + "; no automatic task retry");
            return Translate(result);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(AgentResultKind.Incomplete, "", Detail: "Runtime probe deadline elapsed"); }
        // Caller cancellation propagates so the engine preserves a resumable pending attempt.
        catch (IOException) { return new(AgentResultKind.WorkerFailure, "", Detail: "Enforced launch or collection failed; no native fallback"); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return new(AgentResultKind.Incomplete, "", Detail: "Malformed or incomplete agent protocol"); }
    }
    protected static AgentResult Claim(JsonElement data, string response)
    {
        if (data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Count() != 2
            || !data.TryGetProperty("outcome", out var status) || !data.TryGetProperty("notes", out var notes)
            || notes.ValueKind != JsonValueKind.String || status.ValueKind != JsonValueKind.String) return new(AgentResultKind.Incomplete, response, Detail: "Invalid completion claim schema");
        string claim = status.GetString()!;
        if (claim is not "success" and not "partial_success" and not "fail" and not "retry") return new(AgentResultKind.Incomplete, response, Detail: "Unsupported completion claim");
        return new(claim is "success" or "partial_success" ? AgentResultKind.CompletedClaim : AgentResultKind.TaskFailure, response, claim, "Untrusted worker claim: " + notes.GetString());
    }
    protected static string Prompt(AgentTask t) => t.ApprovedContext + "\n\n" + t.Instructions + "\nReturn only outcome JSON with outcome and notes. Worker success is a claim, not independent verification.";
}
public sealed class CodexAgent(IWorkerLauncher launcher, string executable = "/usr/local/bin/codex", string version = "unverified") : ProcessAgent(launcher, executable, version)
{
    public override AgentCapabilities Capabilities => new("codex", Version);
    public override WorkerCommand BuildCommand(AgentTask task)
    {
        var args = new List<string> { "exec", "--json", "--ephemeral", "--skip-git-repo-check", "--sandbox", "workspace-write", "-C", "/workspace" };
        if (task.Model != null) args.AddRange(["--model", task.Model]);
        args.AddRange(["-c", "model_reasoning_effort=" + JsonSerializer.Serialize(task.ReasoningEffort), "-"]);
        return new(Executable, args.ToImmutableArray(), Prompt(task) + "\nSchema: " + ClaimSchema);
    }
    protected override AgentResult Translate(WorkerResult result)
    {
        bool completed = false; string? claim = null;
        foreach (string line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)) return new(AgentResultKind.Incomplete, result.Stdout, Detail: "Missing event type");
            if (type.GetString() is "error" or "turn.failed") return new(AgentResultKind.TaskFailure, result.Stdout, Detail: "Agent error event; no automatic task retry");
            if (type.GetString() == "turn.completed") completed = true;
            if (type.GetString() == "item.completed" && root.TryGetProperty("item", out var item)
                && item.TryGetProperty("type", out var itemType) && itemType.GetString() == "agent_message"
                && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) claim = text.GetString();
        }
        if (!completed || claim == null) return new(AgentResultKind.Incomplete, result.Stdout, Detail: "Missing terminal event or structured completion claim");
        using var json = JsonDocument.Parse(claim); return Claim(json.RootElement, result.Stdout);
    }
}
public sealed class ClaudeCodeAgent(IWorkerLauncher launcher, string executable = "/usr/local/bin/claude", string version = "unverified") : ProcessAgent(launcher, executable, version)
{
    public override AgentCapabilities Capabilities => new("claude-code", Version);
    public override WorkerCommand BuildCommand(AgentTask task)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--permission-mode", "acceptEdits", "--json-schema", ClaimSchema, "--effort", task.ReasoningEffort };
        if (task.Model != null) args.AddRange(["--model", task.Model]);
        return new(Executable, args.ToImmutableArray(), Prompt(task));
    }
    protected override AgentResult Translate(WorkerResult result)
    {
        JsonElement? final = null;
        foreach (string line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)) return new(AgentResultKind.Incomplete, result.Stdout, Detail: "Missing event type");
            if ((root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True) || type.GetString() == "error")
                return new(AgentResultKind.TaskFailure, result.Stdout, Detail: "Agent error payload; no automatic task retry");
            if (type.GetString() == "result") final = root.Clone();
        }
        if (final == null || !final.Value.TryGetProperty("subtype", out var subtype) || subtype.GetString() != "success"
            || !final.Value.TryGetProperty("structured_output", out var claim)) return new(AgentResultKind.Incomplete, result.Stdout, Detail: "Missing successful structured result");
        return Claim(claim, result.Stdout);
    }
}
// Attractor compatibility facade. Provider receives only immutable task values.
public sealed class AgentBackend(ICodingAgent agent) : ICodergenBackend
{
    public BackendCapabilities Capabilities => new(agent.Capabilities.Name, ["truncate"], Notes: "Fresh isolated coding-agent task; completion is an untrusted claim");
    public async Task<BackendResult> RunAsync(StageRequest request, string prompt, CancellationToken token)
    {
        var result = await agent.ExecuteAsync(new(prompt, request.Graph.Attributes.Text("goal"), request.Handoff.Fidelity,
            request.Node.Attributes.Text("llm_model") is { Length: > 0 } model ? model : null, request.Node.Attributes.Text("reasoning_effort", "high")), token);
        await AtomicFile.WriteJsonAsync(Path.Combine(request.Directory, "agent-result.json"), result, token);
        // Only explicit task retry claims preserve Appendix C retry semantics. Transport failure does not replay a task.
        var outcome = result.Kind == AgentResultKind.CompletedClaim ? new Outcome { Status = result.Claim == "partial_success" ? StageStatus.PartialSuccess : StageStatus.Success, Notes = result.Detail }
            : result.Kind == AgentResultKind.TaskFailure && result.Claim == "retry" ? new Outcome { Status = StageStatus.Retry, FailureReason = result.Detail }
            : Outcome.Fail(result.Kind + ": " + result.Detail);
        return new(result.Response, outcome);
    }
}
