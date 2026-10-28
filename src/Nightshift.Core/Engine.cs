using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Nightshift.Core;

public sealed record EngineOptions(int MaxVisits = 1000, double MaxSeconds = 3600, int? PauseAfterStages = null);
public sealed record RunResult(Outcome Outcome, string State, Checkpoint Checkpoint);
public sealed record Backoff(double InitialMilliseconds = 200, double Factor = 2, double MaxMilliseconds = 60000, bool Jitter = true)
{
    public TimeSpan Delay(int retryNumber) => TimeSpan.FromMilliseconds(Math.Min(MaxMilliseconds, InitialMilliseconds * Math.Pow(Factor, retryNumber - 1)) * (Jitter ? 0.5 + Random.Shared.NextDouble() : 1));
}
public interface IRetryDelay { Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken); }
public sealed class SystemRetryDelay : IRetryDelay { public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken); }

public sealed class Engine(HandlerRegistry handlers, IRetryDelay? retryDelay = null, Backoff? backoff = null)
{
    private readonly IRetryDelay retryDelay = retryDelay ?? new SystemRetryDelay();
    private readonly Backoff backoff = backoff ?? new();
    public static string Hash(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    public async Task<RunResult> RunAsync(string dot, string directory, string workingDirectory, EngineOptions? options = null, bool resume = false, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.MaxVisits <= 0 || options.MaxSeconds <= 0 || !double.IsFinite(options.MaxSeconds) || options.PauseAfterStages is <= 0) throw new ArgumentException("Run limits must be positive and finite");
        var prepared = Preparation.Prepare(dot, handlers); Preparation.RequireValid(prepared); var graph = prepared.Graph;
        using var store = new RunStore(directory);
        Checkpoint state;
        if (resume)
        {
            state = store.Load();
            if (state.SchemaVersion != 1 || state.GraphHash != Hash(dot)) throw new PipelineException("Checkpoint schema or graph hash mismatch");
            if (state.FinalOutcome != null) return new(state.FinalOutcome, state.State, state);
            store.Emit(new(RunEventKind.PipelineResumed));
        }
        else
        {
            if (File.Exists(Path.Combine(store.Root, "manifest.json")) || File.Exists(Path.Combine(store.Root, "checkpoint.json"))) throw new PipelineException("Run directory already initialized; use resume or a new directory");
            state = new() { GraphHash = Hash(dot), NextNode = graph.Nodes.Values.Single(n => n.IsStart).Id };
            await File.WriteAllTextAsync(Path.Combine(store.Root, "graph.dot"), dot, cancellationToken);
            await AtomicFile.WriteJsonAsync(Path.Combine(store.Root, "manifest.json"), new RunManifest(graph.Id, graph.Attributes.Text("goal"), state.GraphHash, Path.GetFullPath(workingDirectory), DateTimeOffset.UtcNow, options), cancellationToken);
            var initial = new RunContext(); foreach (var (key, value) in graph.Attributes) initial.Set("graph." + key, value.Text); initial.Set("run.id", Path.GetFileName(store.Root)); state.ContextValues = initial.Snapshot();
            store.Emit(new(RunEventKind.PipelineStarted, Message: graph.Id)); await store.SaveAsync(state);
        }
        var context = new RunContext(); context.Apply(state.ContextValues);
        double savedSeconds = state.ElapsedSeconds; var stopwatch = Stopwatch.StartNew(); int completedThisSession = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(0.001, options.MaxSeconds - savedSeconds)));
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            state.State = "running";
            while (state.NextNode != null)
            {
                combined.Token.ThrowIfCancellationRequested();
                if (state.PendingVisit == null)
                {
                    if (state.TotalVisits >= options.MaxVisits) return await Finish(Outcome.Fail("Run visit limit reached; incomplete"), "incomplete");
                    state.TotalVisits++; string id = state.NextNode;
                    state.PendingVisit = state.NodeVisits.GetValueOrDefault(id) + 1; state.NodeVisits[id] = state.PendingVisit.Value;
                    state.AttemptOrdinal = 0; state.PendingRetries = 0;
                }
                var node = graph.Nodes[state.NextNode]; context.Set("current_node", node.Id);
                if (node.IsExit)
                {
                    var failed = state.NodeOutcomes.FirstOrDefault(p => graph.Nodes[p.Key].Attributes.Bool("goal_gate") && !p.Value.IsSuccess);
                    if (failed.Key != null)
                    {
                        string? target = Routing.Recovery(graph.Nodes[failed.Key], graph, true);
                        if (target == null) return await Finish(Outcome.Fail($"Goal gate '{failed.Key}' unsatisfied and no recovery target"), "failed");
                        state.Transitions.Add(new(node.Id, target, null, $"unsatisfied goal gate {failed.Key}")); state.NextNode = target; state.IncomingEdgeIndex = null; state.PendingVisit = null;
                        await Save(); continue;
                    }
                    state.CurrentNode = node.Id; state.CompletedNodes.Add(node.Id); state.NextNode = null; state.PendingVisit = null;
                    return await Finish(Outcome.Success("Pipeline reached exit; visited goal gates satisfied"), "success");
                }
                var handoff = Handoff.Resolve(graph, node, state.IncomingEdgeIndex is int ei ? graph.Edges[ei] : null, state.CurrentNode);
                // No backend currently supports serializing live sessions. Unsupported fidelity fails explicitly in codergen.
                int visit = state.PendingVisit.Value;
                Outcome outcome;
                while (true)
                {
                    combined.Token.ThrowIfCancellationRequested(); state.AttemptOrdinal++;
                    context.Set($"internal.retry_count.{node.Id}", state.PendingRetries); state.NodeRetries[node.Id] = state.PendingRetries;
                    await Save(); // Records pending execution, not a completed effect.
                    int attempt = state.AttemptOrdinal; string stageDir = store.StageDirectory(node.Id, visit, attempt);
                    store.Emit(new(RunEventKind.StageStarted, node.Id, visit, attempt, $"type={node.Type}"));
                    using var stageTimeout = CancellationTokenSource.CreateLinkedTokenSource(combined.Token);
                    if (node.Attributes.ContainsKey("timeout")) stageTimeout.CancelAfter(Durations.Parse(node.Attributes.Text("timeout")));
                    var request = new StageRequest(node, graph, context, stageDir, visit, attempt, handoff, store.Emit);
                    try { outcome = await handlers.Resolve(node).ExecuteAsync(request, stageTimeout.Token); stageTimeout.Token.ThrowIfCancellationRequested(); }
                    catch (OperationCanceledException) when (combined.IsCancellationRequested) { throw; }
                    catch (OperationCanceledException) { outcome = new() { Status = StageStatus.Retry, FailureReason = "Stage timeout (transient); external effects may have occurred" }; }
                    catch (TransientException e) { outcome = new() { Status = StageStatus.Retry, FailureReason = e.Message }; }
                    catch (HttpRequestException e) when (e.StatusCode == null || (int)e.StatusCode == 429 || (int)e.StatusCode >= 500) { outcome = new() { Status = StageStatus.Retry, FailureReason = e.Message }; }
                    catch (Exception e) { outcome = Outcome.Fail($"{e.GetType().Name}: {e.Message}"); }
                    await AtomicFile.WriteJsonAsync(Path.Combine(stageDir, "status.json"), outcome);
                    int retries = node.Attributes.Int("max_retries", graph.Attributes.Int("default_max_retries", graph.Attributes.Int("default_max_retry")));
                    bool willRetry = outcome.Status == StageStatus.Retry && state.PendingRetries < retries;
                    store.Emit(new(outcome.IsSuccess ? RunEventKind.StageCompleted : RunEventKind.StageFailed, node.Id, visit, attempt, $"{Conditions.StatusText(outcome.Status)}; will_retry={willRetry}; {outcome.FailureReason}"));
                    if (outcome.Status == StageStatus.Retry)
                    {
                        if (willRetry)
                        {
                            state.PendingRetries++; state.NodeRetries[node.Id] = state.PendingRetries;
                            await Save(); var delay = backoff.Delay(state.PendingRetries); store.Emit(new(RunEventKind.StageRetrying, node.Id, visit, attempt, $"delay_ms={delay.TotalMilliseconds}"));
                            await retryDelay.WaitAsync(delay, combined.Token); continue;
                        }
                        outcome = node.Attributes.Bool("allow_partial") ? outcome with { Status = StageStatus.PartialSuccess, Notes = "Retries exhausted; partial accepted" } : outcome with { Status = StageStatus.Fail, FailureReason = "Max retries exceeded: " + outcome.FailureReason };
                        await AtomicFile.WriteJsonAsync(Path.Combine(stageDir, "effective-outcome.json"), outcome);
                    }
                    break;
                }
                context.Apply(outcome.ContextUpdates); context.Set("outcome", Conditions.StatusText(outcome.Status)); context.Set("preferred_label", outcome.PreferredLabel);
                if (outcome.IsSuccess) { context.Set($"internal.retry_count.{node.Id}", 0); state.NodeRetries[node.Id] = 0; }
                state.CompletedNodes.Add(node.Id); if (outcome.Status != StageStatus.Skipped) state.NodeOutcomes[node.Id] = outcome;
                state.CurrentNode = node.Id;
                Edge? chosen = outcome.Status == StageStatus.Fail ? Routing.Failure(graph.Outgoing(node.Id), outcome, context) : Routing.Select(graph.Outgoing(node.Id), outcome, context);
                string? next = chosen?.To ?? (outcome.Status == StageStatus.Fail ? Routing.Recovery(node, graph, false) : null);
                state.NextNode = next; state.IncomingEdgeIndex = chosen == null ? null : graph.Edges.IndexOf(chosen); state.PendingVisit = null;
                if (next != null)
                {
                    state.Transitions.Add(new(node.Id, next, state.IncomingEdgeIndex, chosen != null ? "edge" : "failure recovery"));
                    store.Emit(new(RunEventKind.TransitionChosen, node.Id, visit, state.AttemptOrdinal, $"{node.Id} -> {next}"));
                }
                await Save(); completedThisSession++;
                if (next == null) return await Finish(outcome.Status == StageStatus.Fail ? outcome : Outcome.Fail("No eligible edge before terminal; incomplete"), "failed");
                if (options.PauseAfterStages != null && completedThisSession >= options.PauseAfterStages) return await Interrupt("Paused after requested completed stages");
            }
            return await Finish(Outcome.Fail("Missing continuation"), "failed");
        }
        catch (OperationCanceledException)
        {
            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested) return await Finish(Outcome.Fail("Run time limit reached; incomplete"), "incomplete");
            return await Interrupt("Cancelled; pending stage can replay on resume");
        }
        catch (Exception e) { return await Finish(Outcome.Fail($"Pipeline error: {e.GetType().Name}: {e.Message}"), "failed"); }

        async Task Save() { state.ContextValues = context.Snapshot(); state.ElapsedSeconds = savedSeconds + stopwatch.Elapsed.TotalSeconds; await store.SaveAsync(state); }
        async Task<RunResult> Finish(Outcome outcome, string status)
        {
            state.State = status; state.FinalOutcome = outcome; await Save();
            store.Emit(new(outcome.IsSuccess ? RunEventKind.PipelineCompleted : RunEventKind.PipelineFailed, state.CurrentNode, Message: outcome.Notes + outcome.FailureReason)); return new(outcome, status, state);
        }
        async Task<RunResult> Interrupt(string reason) { state.State = "interrupted"; await Save(); store.Emit(new(RunEventKind.PipelineInterrupted, state.NextNode ?? "", Message: reason)); return new(Outcome.Fail(reason), state.State, state); }
    }
}
