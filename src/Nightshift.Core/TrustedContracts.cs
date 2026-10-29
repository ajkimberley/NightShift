using System.Collections.Immutable;

namespace Nightshift.Core;

// These values cross the orchestration boundary; no mutable graph, callbacks or host paths.
public sealed record AgentCapabilities(string Name, string Version, bool FreshTasks = true,
    bool StructuredClaims = true, bool Progress = true, bool Cancellation = true,
    bool Sessions = false, bool Steering = false, bool Hooks = false);
public sealed record AgentTask(string Instructions, string ApprovedContext, string Fidelity = "truncate",
    string? Model = null, string ReasoningEffort = "high", bool SessionRequested = false,
    bool SteeringRequested = false, bool HooksRequested = false);
public enum AgentResultKind { CompletedClaim, TaskFailure, Incomplete, Cancelled, WorkerFailure, ConfigurationError }
public sealed record AgentResult(AgentResultKind Kind, string Response, string Claim = "fail", string Detail = "");
public interface ICodingAgent
{
    AgentCapabilities Capabilities { get; }
    Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken token);
}
public sealed record WorkerCommand(string Executable, ImmutableArray<string> Arguments, string Input = "");
public sealed record WorkerResult(int ExitCode, string Stdout, string Stderr, string ExecutionId);
public sealed record RuntimeIdentity(string Profile, string RuntimeVersion, string Connection, string Image, string Platform);
public interface IWorkerLauncher
{
    Task<RuntimeIdentity> VerifyAsync(CancellationToken token);
    Task<WorkerResult> ExecuteAsync(WorkerCommand command, CancellationToken token);
}

public sealed record RepositoryIdentity(string Service, string Namespace, string Name);
public sealed record Candidate(RepositoryIdentity Repository, string Revision, string SnapshotDigest);
public sealed record PullRequestIdentity(string Id, string Url, string CandidateRevision, string Correlation);
public sealed record PipelineDefinition(string Id, string Revision, string Ref);
public enum CheckState { Pending, Running, Passed, Failed, Cancelled, Unknown }
public sealed record PipelineRun(string Id, string Url, string CandidateRevision, string PipelineId,
    string Correlation, CheckState State);
public sealed record ArtifactReference(string Id, string Name, string Url);
public sealed record PipelineOutput(string Log, ImmutableArray<ArtifactReference> Artifacts);
public interface IRepositoryService
{
    string Name { get; }
    Task<PullRequestIdentity?> FindPullRequestAsync(Candidate candidate, string correlation, CancellationToken token);
    Task<PullRequestIdentity> CreatePullRequestAsync(Candidate candidate, string sourceRef, string targetRef, string correlation, CancellationToken token);
}
public interface IPipelineService
{
    string Name { get; }
    Task<PipelineRun?> ReconcileAsync(Candidate candidate, PipelineDefinition pipeline, string correlation, CancellationToken token);
    Task<PipelineRun?> RequestAsync(Candidate candidate, PipelineDefinition pipeline, string correlation, CancellationToken token);
    Task<PipelineRun> ObserveAsync(Candidate candidate, PipelineDefinition pipeline, PipelineRun run, CancellationToken token);
    Task CancelAsync(PipelineRun run, CancellationToken token);
    Task<PipelineOutput> OutputAsync(PipelineRun run, CancellationToken token);
}
public enum EvidenceSource { WorkerClaim, ControlledLocalCheck, HostedCi, HumanDecision }
public sealed record VerificationEvidence(EvidenceSource Source, Candidate Candidate, string PolicyRevision,
    string CheckId, CheckState State, string ExecutionId, DateTimeOffset RecordedAt);
public sealed record AcceptanceDecision(bool Accepted, string Reason);
public static class Acceptance
{
    public static AcceptanceDecision Decide(Candidate candidate, string policy, IEnumerable<string> checks,
        IEnumerable<VerificationEvidence> evidence)
    {
        var required = checks.ToArray();
        if (required.Length == 0) return new(false, "No approved checks");
        foreach (string check in required)
        {
            var matching = evidence.Where(e => e.Source is EvidenceSource.ControlledLocalCheck or EvidenceSource.HostedCi
                && e.Candidate == candidate && e.PolicyRevision == policy && e.CheckId == check).ToArray();
            if (matching.Length == 0 || matching[^1].State != CheckState.Passed)
                return new(false, "Missing, stale, mismatched or nonpassing evidence: " + check);
        }
        return new(true, "Specified approved checks passed for the exact candidate; no general correctness claim");
    }
}
public interface IRunStore : IDisposable
{
    string Root { get; }
    event Action<RunEvent>? Event;
    void Emit(RunEvent evt);
    Task SaveAsync(Checkpoint state);
    Checkpoint Load();
    string StageDirectory(string node, int visit, int attempt);
}
