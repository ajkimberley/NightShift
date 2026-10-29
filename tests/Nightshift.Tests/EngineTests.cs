using System.Net;
using System.Text.Json;
using Nightshift.Core;

namespace Nightshift.Tests;

public sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.test-runs", Guid.NewGuid().ToString("N")));
    public TestWorkspace() => Directory.CreateDirectory(Root);
    public string RunDirectory(string name = "run") => Path.Combine(Root, name);
    public void Dispose() { Directory.Delete(Root, true); }
}
public sealed class NoWait : IRetryDelay
{
    public List<TimeSpan> Delays { get; } = [];
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Delays.Add(delay); return Task.CompletedTask; }
}
public sealed class CallbackBackend(Func<StageRequest, CancellationToken, Task<BackendResult>> action, string[]? modes = null) : ICodergenBackend
{
    public BackendCapabilities Capabilities { get; } = new("test", modes ?? Handoff.Modes);
    public Task<BackendResult> RunAsync(StageRequest r, string prompt, CancellationToken token) => action(r, token);
}

public class EngineTests
{
    internal static string StageGraph(string attributes = "", string extra = "") => $"digraph G {{ start [shape=Mdiamond] work [prompt=do_work {attributes}] exit [shape=Msquare] start -> work work -> exit {extra} }}";
    internal static HandlerRegistry Registry(string workdir, ICodergenBackend? backend = null, IInterviewer? interviewer = null) => HandlerRegistry.Create(backend ?? new ScriptedBackend(), interviewer ?? new QueueInterviewer([]), workdir, new ToolHandler(workdir));
    [Fact]
    public async Task BareStartAndEndIdentifiersResolveToNoOpHandlersAndPublishTypedEvents()
    {
        using var w = new TestWorkspace(); int calls = 0;
        var backend = new CallbackBackend((_, _) => { calls++; return Task.FromResult(new BackendResult("", Outcome.Success())); });
        var engine = new Engine(Registry(w.Root, backend)); var events = new List<RunEvent>(); engine.Event += events.Add;
        var r = await engine.RunAsync("digraph G { start -> work -> end }", w.RunDirectory(), w.Root);
        Assert.True(r.Outcome.IsSuccess); Assert.Equal(1, calls);
        Assert.Contains(events, e => e.Kind == RunEventKind.StageStarted && e.Node == "start");
        Assert.Contains(events, e => e.Kind == RunEventKind.PipelineCompleted); Assert.All(events, e => Assert.NotEqual(default, e.Timestamp));
    }
    [Fact]
    public async Task MultiStageRunPersistsStatusContextTransitionsAndFinalCheckpoint()
    {
        using var w = new TestWorkspace();
        string graph = "digraph G { graph [goal=business] start [shape=Mdiamond] a [prompt=\"$goal\", script.set.fact=true] b [prompt=read] exit [shape=Msquare] start -> a -> b -> exit }";
        var result = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root);
        Assert.True(result.Outcome.IsSuccess); Assert.Equal(new[] { "start", "a", "b", "exit" }, result.Checkpoint.CompletedNodes);
        Assert.Equal("exit", result.Checkpoint.CurrentNode); Assert.Null(result.Checkpoint.NextNode);
        Assert.Equal("true", result.Checkpoint.ContextValues["fact"].GetString());
        Assert.Equal(3, result.Checkpoint.Transitions.Count);
        var status = Directory.GetFiles(w.RunDirectory(), "status.json", SearchOption.AllDirectories).Single(f => f.Contains("/a/"));
        string json = await File.ReadAllTextAsync(status); Assert.Contains("\"outcome\": \"success\"", json); Assert.DoesNotContain("\"status\"", json);
        Assert.Equal("business", await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(status)!, "prompt.md")));
        var saved = Json.Read<Checkpoint>(await File.ReadAllTextAsync(Path.Combine(w.RunDirectory(), "checkpoint.json")));
        Assert.Equal(StageStatus.Success, saved.FinalOutcome!.Status);
        var again = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root, resume: true);
        Assert.Equal(saved.Revision, again.Checkpoint.Revision);
    }
    [Fact]
    public async Task FailedCheckRepairPassIsARevisitNotAHandlerRetry()
    {
        using var w = new TestWorkspace(); string graph = "digraph G { start [shape=Mdiamond] check [prompt=check, goal_gate=true, script.outcomes=\"fail,success\"] repair [prompt=repair] exit [shape=Msquare] start -> check check -> repair [condition=\"outcome=fail\"] repair -> check check -> exit [condition=\"outcome=success\"] }";
        var result = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root);
        Assert.True(result.Outcome.IsSuccess); Assert.Equal(new[] { "start", "check", "repair", "check", "exit" }, result.Checkpoint.CompletedNodes);
        Assert.Equal(2, result.Checkpoint.NodeVisits["check"]); Assert.Equal(0, result.Checkpoint.NodeRetries["check"]);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(w.RunDirectory(), "check"), "status.json", SearchOption.AllDirectories).Length);
    }
    [Fact]
    public async Task RetryMeansAdditionalAttemptsWithBackoffAndRetainedArtifacts()
    {
        using var w = new TestWorkspace(); var delay = new NoWait();
        var result = await new Engine(Registry(w.Root), delay, new(100, 2, 1000, false)).RunAsync(StageGraph(", max_retries=2, script.outcomes=\"retry,retry,success\""), w.RunDirectory(), w.Root);
        Assert.True(result.Outcome.IsSuccess); Assert.Equal(new[] { 100d, 200d }, delay.Delays.Select(x => x.TotalMilliseconds));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(w.RunDirectory(), "work"), "status.json", SearchOption.AllDirectories).Length);
        Assert.Equal(1, result.Checkpoint.NodeVisits["work"]);
    }
    [Theory]
    [InlineData(false, StageStatus.Fail)][InlineData(true, StageStatus.PartialSuccess)]
    public async Task RetryExhaustionFailsUnlessPartialExplicitlyAccepted(bool partial, StageStatus expected)
    {
        using var w = new TestWorkspace();
        var r = await new Engine(Registry(w.Root), new NoWait()).RunAsync(StageGraph($", max_retries=1, script.outcomes=retry, allow_partial={partial.ToString().ToLowerInvariant()}"), w.RunDirectory(), w.Root);
        Assert.Equal(expected, r.Checkpoint.NodeOutcomes["work"].Status); Assert.Equal(partial, r.Outcome.IsSuccess);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(w.RunDirectory(), "work"), "status.json", SearchOption.AllDirectories).Length);
    }
    [Theory]
    [InlineData("transient", 3, true)][InlineData("429", 3, true)][InlineData("503", 3, true)][InlineData("401", 1, false)][InlineData("configuration", 1, false)][InlineData("fail", 1, false)]
    public async Task ErrorsKeepTransientVersusTerminalClassification(string kind, int expectedCalls, bool success)
    {
        using var w = new TestWorkspace(); int calls = 0;
        var backend = new CallbackBackend((_, _) =>
        {
            calls++;
            if (calls == 3) return Task.FromResult(new BackendResult("done", Outcome.Success()));
            if (int.TryParse(kind, out int code)) throw new HttpRequestException("HTTP error", null, (HttpStatusCode)code);
            if (kind == "transient") throw new TransientException("temporary");
            if (kind == "configuration") throw new ArgumentException("bad config");
            return Task.FromResult(new BackendResult("failure", Outcome.Fail("terminal")));
        });
        var r = await new Engine(Registry(w.Root, backend), new NoWait()).RunAsync(StageGraph(", max_retries=2"), w.RunDirectory(), w.Root);
        Assert.Equal(expectedCalls, calls); Assert.Equal(success, r.Outcome.IsSuccess);
    }
    [Fact]
    public async Task FailNeverUsesUnrelatedUnconditionalSuccessEdge()
    {
        using var w = new TestWorkspace();
        var r = await new Engine(Registry(w.Root)).RunAsync(StageGraph(", script.outcomes=fail"), w.RunDirectory(), w.Root);
        Assert.False(r.Outcome.IsSuccess); Assert.DoesNotContain("exit", r.Checkpoint.CompletedNodes);
    }
    [Fact]
    public async Task GoalGateBlocksExitAndMissingRecoveryFails()
    {
        using var w = new TestWorkspace();
        string graph = StageGraph(", goal_gate=true, script.outcomes=fail", "work -> exit [condition=\"outcome=fail\"]");
        var r = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root);
        Assert.False(r.Outcome.IsSuccess); Assert.Contains("Goal gate", r.Outcome.FailureReason);
    }
    [Fact]
    public async Task GoalGateRecoveryFallsThroughInvalidTargetsAndAllowsPartialSuccess()
    {
        using var w = new TestWorkspace();
        string graph = StageGraph(", goal_gate=true, script.outcomes=\"fail,partial_success\", retry_target=missing", "graph [fallback_retry_target=work] work -> exit [condition=\"outcome=fail\"]");
        var r = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root);
        Assert.True(r.Outcome.IsSuccess); Assert.Equal(2, r.Checkpoint.NodeVisits["work"]);
        Assert.Contains(r.Checkpoint.Transitions, t => t.Reason.Contains("goal gate"));
    }
    [Fact]
    public async Task UnvisitedGoalGatesAreNotAnArtifactAcceptancePolicy()
    {
        using var w = new TestWorkspace();
        string graph = "digraph G { start [shape=Mdiamond] unused [goal_gate=true, prompt=unused] exit [shape=Msquare] start -> exit [weight=2] start -> unused unused -> exit }";
        var r = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root); Assert.True(r.Outcome.IsSuccess); Assert.False(r.Checkpoint.NodeOutcomes.ContainsKey("unused"));
    }
    [Fact]
    public async Task ConditionalOutcomeIsItsOwnSuccessBusinessFactCarriesPriorResult()
    {
        using var w = new TestWorkspace();
        string graph = "digraph G { start [shape=Mdiamond] check [prompt=check, script.set.tests_passed=false] gate [shape=diamond] repair [prompt=repair] exit [shape=Msquare] start -> check -> gate gate -> repair [condition=\"context.tests_passed=false\"] gate -> exit [condition=\"context.tests_passed=true\"] repair -> exit }";
        var r = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root);
        Assert.True(r.Outcome.IsSuccess); Assert.Equal(StageStatus.Success, r.Checkpoint.NodeOutcomes["gate"].Status); Assert.Contains("repair", r.Checkpoint.CompletedNodes);
    }
    [Fact]
    public async Task RunVisitAndTimeLimitsAreNeverSuccess()
    {
        using var w = new TestWorkspace(); string graph = StageGraph("", "work -> work [weight=2]");
        var r = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root, new(MaxVisits: 4));
        Assert.Equal("incomplete", r.State); Assert.False(r.Outcome.IsSuccess); Assert.Equal(4, r.Checkpoint.TotalVisits);
        var backend = new CallbackBackend(async (_, token) => { await Task.Delay(10000, token); return new("", Outcome.Success()); });
        var timed = await new Engine(Registry(w.Root, backend)).RunAsync(StageGraph(), w.RunDirectory("timed"), w.Root, new(MaxSeconds: .1));
        Assert.Equal("incomplete", timed.State);
    }
    [Fact]
    public async Task ResumeAfterCompletionPreservesOutcomesAndDoesNotSkipIntentionalRevisits()
    {
        using var w = new TestWorkspace(); string graph = StageGraph(", script.outcomes=\"fail,success\", retry_target=work");
        var engine = new Engine(Registry(w.Root));
        var paused = await engine.RunAsync(graph, w.RunDirectory(), w.Root, new(PauseAfterStages: 2));
        Assert.Equal("interrupted", paused.State); Assert.Equal("work", paused.Checkpoint.NextNode); Assert.Equal(StageStatus.Fail, paused.Checkpoint.NodeOutcomes["work"].Status);
        string prior = Directory.GetFiles(Path.Combine(w.RunDirectory(), "work"), "status.json", SearchOption.AllDirectories).Single(); string content = await File.ReadAllTextAsync(prior);
        var resumed = await engine.RunAsync(graph, w.RunDirectory(), w.Root, resume: true);
        Assert.True(resumed.Outcome.IsSuccess); Assert.Equal(2, resumed.Checkpoint.NodeVisits["work"]); Assert.Equal(content, await File.ReadAllTextAsync(prior));
    }
    [Fact]
    public async Task CancellationBeforeCompletionReplaysPendingStageWithNewArtifacts()
    {
        using var w = new TestWorkspace(); using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new CallbackBackend(async (r, token) => { await File.WriteAllTextAsync(Path.Combine(r.Directory, "effect.txt"), "effect may have happened", token); started.SetResult(); await Task.Delay(10000, token); return new("", Outcome.Success()); });
        var task = new Engine(Registry(w.Root, first)).RunAsync(StageGraph(), w.RunDirectory(), w.Root, cancellationToken: cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel(); var stopped = await task;
        Assert.Equal("interrupted", stopped.State); Assert.Equal("work", stopped.Checkpoint.NextNode); Assert.DoesNotContain("work", stopped.Checkpoint.CompletedNodes);
        var r = await new Engine(Registry(w.Root)).RunAsync(StageGraph(), w.RunDirectory(), w.Root, resume: true);
        Assert.True(r.Outcome.IsSuccess); Assert.Single(Directory.GetFiles(w.RunDirectory(), "effect.txt", SearchOption.AllDirectories));
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(w.RunDirectory(), "work", "visit-0001")).Length);
    }
    [Fact]
    public async Task ResumeRejectsChangedGraphAndRunDirectoryReuse()
    {
        using var w = new TestWorkspace(); var engine = new Engine(Registry(w.Root)); string graph = StageGraph();
        await engine.RunAsync(graph, w.RunDirectory(), w.Root, new(PauseAfterStages: 1));
        await Assert.ThrowsAsync<PipelineException>(() => engine.RunAsync(graph + " ", w.RunDirectory(), w.Root, resume: true));
        await Assert.ThrowsAsync<PipelineException>(() => engine.RunAsync(graph, w.RunDirectory(), w.Root));
        using var store = new RunStore(w.RunDirectory()); Assert.Throws<IOException>(() => new RunStore(w.RunDirectory()));
    }
    [Fact]
    public async Task MissingStatusTextDoesNotBecomeVerifiedSuccessAndAutoStatusIsExplicit()
    {
        using var w = new TestWorkspace(); var b = new CallbackBackend((_, _) => Task.FromResult(new BackendResult("fluent completion", null)));
        var engine = new Engine(Registry(w.Root, b));
        var r = await engine.RunAsync(StageGraph(), w.RunDirectory(), w.Root); Assert.False(r.Outcome.IsSuccess);
        var auto = await engine.RunAsync(StageGraph(", auto_status=true"), w.RunDirectory("auto"), w.Root); Assert.True(auto.Outcome.IsSuccess); Assert.Contains("auto-status", auto.Checkpoint.NodeOutcomes["work"].Notes);
    }
    [Fact]
    public async Task UnsupportedFidelityFailsAndCustomHandlerReplacementExecutes()
    {
        using var w = new TestWorkspace(); var b = new CallbackBackend((_, _) => throw new Exception("must not run"), ["truncate"]);
        var r = await new Engine(Registry(w.Root, b)).RunAsync(StageGraph(", fidelity=full"), w.RunDirectory(), w.Root);
        Assert.Contains("does not implement fidelity", r.Outcome.FailureReason);
        var registry = Registry(w.Root); registry.Register("custom", new DelegateHandler((_, _) => throw new Exception("replaced")));
        registry.Register("custom", new DelegateHandler((r, _) => { r.Context.Set("direct", true); return Task.FromResult(Outcome.Success()); }));
        var custom = await new Engine(registry).RunAsync(StageGraph(", type=custom"), w.RunDirectory("custom"), w.Root);
        Assert.True(custom.Outcome.IsSuccess); Assert.True(custom.Checkpoint.ContextValues["direct"].GetBoolean());
    }
    [Fact]
    public void StatusRequiresExternalOutcomeAndRejectsInvalidEnum()
    {
        Assert.Throws<JsonException>(() => Json.Read<Outcome>("{\"status\":\"success\"}"));
        Assert.Throws<JsonException>(() => Json.Read<Outcome>("{\"outcome\":\"invented\"}"));
        var o = Json.Read<Outcome>("{\"outcome\":\"partial_success\",\"context_updates\":{\"n\":3}}"); Assert.Equal(StageStatus.PartialSuccess, o.Status); Assert.Equal(3, o.ContextUpdates["n"].GetInt32());
    }
    [Fact]
    public async Task TenPlusNodePipelineAndLegacyRetryDefaultWork()
    {
        using var w = new TestWorkspace(); string nodes = string.Join(" ", Enumerable.Range(0, 12).Select(i => $"n{i} [prompt=x]")); string chain = string.Join(" -> ", Enumerable.Range(0, 12).Select(i => $"n{i}"));
        var r = await new Engine(Registry(w.Root)).RunAsync($"digraph G {{ graph [default_max_retry=1] start [shape=Mdiamond] exit [shape=Msquare] {nodes} start -> {chain} -> exit }}", w.RunDirectory(), w.Root);
        Assert.True(r.Outcome.IsSuccess); Assert.Equal(14, r.Checkpoint.CompletedNodes.Count);
        var legacy = await new Engine(Registry(w.Root), new NoWait()).RunAsync(StageGraph(", script.outcomes=\"retry,success\"", "graph [default_max_retry=1]"), w.RunDirectory("legacy"), w.Root); Assert.True(legacy.Outcome.IsSuccess);
    }
    [Fact]
    public async Task NoEligibleEdgeCannotBypassTerminalAndGoalChecks()
    {
        using var w = new TestWorkspace();
        string graph = "digraph G { start [shape=Mdiamond] work [prompt=x] exit [shape=Msquare] start -> work work -> exit [condition=\"outcome=fail\"] }";
        var r = await new Engine(Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root);
        Assert.False(r.Outcome.IsSuccess); Assert.Contains("No eligible edge", r.Outcome.FailureReason);
    }
    [Fact]
    public async Task ResumeKeepsUnsatisfiedGoalGateOutcomeAtExit()
    {
        using var w = new TestWorkspace();
        string graph = StageGraph(", goal_gate=true, script.outcomes=fail", "work -> exit [condition=\"outcome=fail\"]");
        var engine = new Engine(Registry(w.Root));
        var paused = await engine.RunAsync(graph, w.RunDirectory(), w.Root, new(PauseAfterStages: 2));
        Assert.Equal("exit", paused.Checkpoint.NextNode);
        var resumed = await engine.RunAsync(graph, w.RunDirectory(), w.Root, resume: true);
        Assert.False(resumed.Outcome.IsSuccess); Assert.Contains("Goal gate", resumed.Outcome.FailureReason);
    }
    private sealed class CancelDelay(CancellationTokenSource cancel) : IRetryDelay
    {
        public Task WaitAsync(TimeSpan delay, CancellationToken token) { cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    [Fact]
    public async Task ResumeKeepsPendingRetryBudgetAndAttemptEvidence()
    {
        using var w = new TestWorkspace(); using var cancel = new CancellationTokenSource();
        string graph = StageGraph(", max_retries=1, script.outcomes=\"retry,success\"");
        var first = await new Engine(Registry(w.Root), new CancelDelay(cancel)).RunAsync(graph, w.RunDirectory(), w.Root, cancellationToken: cancel.Token);
        Assert.Equal("interrupted", first.State); Assert.Equal(1, first.Checkpoint.PendingRetries);
        var resumed = await new Engine(Registry(w.Root), new NoWait()).RunAsync(graph, w.RunDirectory(), w.Root, resume: true);
        Assert.True(resumed.Outcome.IsSuccess); Assert.Equal(2, Directory.GetFiles(Path.Combine(w.RunDirectory(), "work"), "status.json", SearchOption.AllDirectories).Length);
    }

}
