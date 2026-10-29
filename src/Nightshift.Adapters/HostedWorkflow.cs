using Nightshift.Core;

namespace Nightshift.Adapters;

public sealed record HostedIntent(Candidate Candidate, PipelineDefinition Pipeline, string SourceRef, string TargetRef,
    string RepositoryAdapter, string PipelineAdapter, string Correlation, bool PrDispatched = false,
    PullRequestIdentity? PullRequest = null, bool CheckDispatched = false, PipelineRun? Run = null, string State = "pending");

// Called with coordinator-owned storage. A lease serializes resumption and side effects.
public sealed class HostedWorkflow(IRepositoryService repository, IPipelineService pipeline)
{
    public async Task<HostedIntent> AdvanceAsync(string directory, Candidate candidate, PipelineDefinition definition,
        string sourceRef, string targetRef, CancellationToken token)
    {
        Directory.CreateDirectory(directory); WorkspaceSnapshot.RejectLinks(directory);
        using var lease = new FileStream(Path.Combine(directory, ".effects.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string path = Path.Combine(directory, "intent.json");
        var intent = File.Exists(path) ? Json.Read<HostedIntent>(await File.ReadAllTextAsync(path, token))
            : new(candidate, definition, sourceRef, targetRef, repository.Name, pipeline.Name, Guid.NewGuid().ToString("N"));
        if (intent.Candidate != candidate || intent.Pipeline != definition || intent.SourceRef != sourceRef || intent.TargetRef != targetRef
            || intent.RepositoryAdapter != repository.Name || intent.PipelineAdapter != pipeline.Name)
            throw new ServiceException(ServiceFailure.Configuration, "Saved hosted intent identity cannot change on resume");
        await Save(); // Correlation exists before any network side effect.
        if (intent.PullRequest == null)
        {
            var found = await repository.FindPullRequestAsync(candidate, intent.Correlation, token);
            if (found == null)
            {
                if (intent.PrDispatched) return await Uncertain("Pull request dispatch cannot yet be reconciled");
                intent = intent with { PrDispatched = true }; await Save();
                found = await repository.CreatePullRequestAsync(candidate, sourceRef, targetRef, intent.Correlation, token);
            }
            intent = intent with { PullRequest = found }; await Save();
        }
        if (intent.Run == null)
        {
            var found = await pipeline.ReconcileAsync(candidate, definition, intent.Correlation, token);
            if (found == null)
            {
                if (intent.CheckDispatched) return await Uncertain("Pipeline dispatch cannot yet be reconciled");
                intent = intent with { CheckDispatched = true }; await Save();
                found = await pipeline.RequestAsync(candidate, definition, intent.Correlation, token);
            }
            intent = intent with { Run = found }; await Save();
            if (found == null) return await Uncertain("Dispatched check has no safely correlated native identity yet");
        }
        var observed = await pipeline.ObserveAsync(candidate, definition, intent.Run!, token);
        intent = intent with { Run = observed, State = observed.State.ToString().ToLowerInvariant() }; await Save(); return intent;
        Task Save() => AtomicFile.WriteJsonAsync(path, intent, CancellationToken.None);
        async Task<HostedIntent> Uncertain(string reason) { intent = intent with { State = "uncertain: " + reason }; await Save(); return intent; }
    }
    public static VerificationEvidence Evidence(HostedIntent intent, string policyRevision)
    {
        if (intent.Run == null || intent.Run.CandidateRevision != intent.Candidate.Revision || intent.Run.PipelineId != intent.Pipeline.Id
            || intent.Run.Correlation != intent.Correlation) throw new ServiceException(ServiceFailure.Protocol, "Cannot record mismatched or missing check identity");
        return new(EvidenceSource.HostedCi, intent.Candidate, policyRevision, "hosted:" + intent.Pipeline.Id,
            intent.Run.State, intent.PipelineAdapter + ":" + intent.Run.Id, DateTimeOffset.UtcNow);
    }
}
