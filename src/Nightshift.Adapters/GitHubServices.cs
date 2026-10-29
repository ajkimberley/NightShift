using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Nightshift.Core;
using static Nightshift.Adapters.ServiceHttp;

namespace Nightshift.Adapters;

public sealed class GitHubRepository(ServiceHttp http, RepositoryIdentity repository) : IRepositoryService
{
    public string Name => "github";
    private string Root => $"repos/{Segment(repository.Namespace)}/{Segment(repository.Name)}";
    private void CandidateMatches(Candidate candidate) => Correlate(candidate.Repository == repository);
    public async Task<PullRequestIdentity?> FindPullRequestAsync(Candidate candidate, string correlation, CancellationToken token)
    {
        CandidateMatches(candidate);
        var data = await http.Json(HttpMethod.Get, Root + "/pulls?state=all&per_page=100", null, token);
        var matches = data.RequiredArray().Where(p => Text(p, "body").Contains("[nightshift:" + correlation + "]", StringComparison.Ordinal)
            && Text(p.Required("head"), "sha") == candidate.Revision).ToArray();
        if (matches.Length > 1) throw new ServiceException(ServiceFailure.Uncertain, "Multiple correlated pull requests; operator reconciliation required");
        return matches.Length == 0 ? null : Map(matches[0], candidate, correlation);
    }
    public async Task<PullRequestIdentity> CreatePullRequestAsync(Candidate candidate, string sourceRef, string targetRef, string correlation, CancellationToken token)
    {
        CandidateMatches(candidate);
        var head = await http.Json(HttpMethod.Get, Root + "/git/ref/heads/" + Segment(sourceRef), null, token);
        Correlate(Text(head.Required("object"), "sha") == candidate.Revision);
        var data = await http.Json(HttpMethod.Post, Root + "/pulls", new { title = "Nightshift candidate " + candidate.Revision, head = sourceRef, @base = targetRef, body = "[nightshift:" + correlation + "]" }, token);
        return Map(data, candidate, correlation);
    }
    private static PullRequestIdentity Map(JsonElement data, Candidate candidate, string correlation)
    {
        Correlate(Text(data.Required("head"), "sha") == candidate.Revision
            && Text(data.Required("head").Required("repo"), "full_name") == candidate.Repository.Namespace + "/" + candidate.Repository.Name
            && Text(data, "body").Contains("[nightshift:" + correlation + "]", StringComparison.Ordinal));
        return new(Text(data, "number"), Text(data, "html_url"), candidate.Revision, correlation);
    }
}
public sealed class GitHubActions(ServiceHttp http, RepositoryIdentity repository) : IPipelineService
{
    public string Name => "github-actions";
    private string Root => $"repos/{Segment(repository.Namespace)}/{Segment(repository.Name)}/actions";
    private async Task<string> Approved(Candidate candidate, PipelineDefinition pipeline, CancellationToken token)
    {
        Correlate(candidate.Repository == repository);
        var workflow = await http.Json(HttpMethod.Get, Root + "/workflows/" + Segment(pipeline.Id), null, token);
        string nativeId = Text(workflow, "id"), path = Text(workflow, "path");
        var contents = await http.Json(HttpMethod.Get, $"repos/{Segment(repository.Namespace)}/{Segment(repository.Name)}/contents/{string.Join('/', path.Split('/').Select(Segment))}?ref={Segment(candidate.Revision)}", null, token);
        byte[] bytes = Convert.FromBase64String(Text(contents, "content").Replace("\n", "", StringComparison.Ordinal));
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        Correlate(pipeline.Revision == nativeId + ":" + digest && Text(contents, "encoding") == "base64");
        return nativeId;
    }
    public async Task<PipelineRun?> RequestAsync(Candidate candidate, PipelineDefinition pipeline, string correlation, CancellationToken token)
    {
        await Approved(candidate, pipeline, token);
        var response = await http.Send(HttpMethod.Post, Root + "/workflows/" + Segment(pipeline.Id) + "/dispatches",
            new { @ref = pipeline.Ref, inputs = new { nightshift_correlation = correlation, nightshift_candidate = candidate.Revision } }, token);
        if ((int)response.Status >= 300) throw new ServiceException(ServiceFailure.Protocol, "Dispatch redirect refused");
        return await ReconcileAsync(candidate, pipeline, correlation, token);
    }
    public async Task<PipelineRun?> ReconcileAsync(Candidate candidate, PipelineDefinition pipeline, string correlation, CancellationToken token)
    {
        string nativeId = await Approved(candidate, pipeline, token);
        var data = await http.Json(HttpMethod.Get, Root + "/workflows/" + Segment(pipeline.Id) + "/runs?event=workflow_dispatch&head_sha=" + Segment(candidate.Revision) + "&per_page=100", null, token);
        var matches = data.Required("workflow_runs").RequiredArray().Where(r => Text(r, "head_sha") == candidate.Revision
            && Text(r, "display_title") == "nightshift-" + correlation && Text(r, "workflow_id") == nativeId).ToArray();
        if (matches.Length > 1) throw new ServiceException(ServiceFailure.Uncertain, "Multiple correlated workflow runs");
        return matches.Length == 0 ? null : Map(matches[0], candidate, pipeline, correlation, nativeId);
    }
    public async Task<PipelineRun> ObserveAsync(Candidate candidate, PipelineDefinition pipeline, PipelineRun run, CancellationToken token)
    {
        var data = await http.Json(HttpMethod.Get, Root + "/runs/" + Segment(run.Id), null, token);
        var mapped = Map(data, candidate, pipeline, run.Correlation, await Approved(candidate, pipeline, token));
        Correlate(mapped.Id == run.Id); return mapped;
    }
    private PipelineRun Map(JsonElement data, Candidate candidate, PipelineDefinition pipeline, string correlation, string nativeId)
    {
        Correlate(Text(data, "head_sha") == candidate.Revision && Text(data, "workflow_id") == nativeId
            && Text(data, "display_title") == "nightshift-" + correlation && Text(data, "event") == "workflow_dispatch"
            && Text(data.Required("repository"), "full_name") == repository.Namespace + "/" + repository.Name);
        string status = Text(data, "status"), conclusion = Text(data, "conclusion");
        var state = status switch { "queued" or "waiting" or "requested" or "pending" => CheckState.Pending, "in_progress" => CheckState.Running,
            "completed" => conclusion switch { "success" => CheckState.Passed, "failure" or "timed_out" or "action_required" => CheckState.Failed, "cancelled" => CheckState.Cancelled, _ => CheckState.Unknown }, _ => CheckState.Unknown };
        return new(Text(data, "id"), Text(data, "html_url"), candidate.Revision, pipeline.Id, correlation, state);
    }
    public async Task CancelAsync(PipelineRun run, CancellationToken token)
    {
        var response = await http.Send(HttpMethod.Post, Root + "/runs/" + Segment(run.Id) + "/cancel", null, token);
        if ((int)response.Status >= 300) throw new ServiceException(ServiceFailure.Protocol, "Cancellation redirect refused");
    }
    public async Task<PipelineOutput> OutputAsync(PipelineRun run, CancellationToken token)
    {
        string log = await http.Logs(Root + "/runs/" + Segment(run.Id) + "/logs", true, token);
        var data = await http.Json(HttpMethod.Get, Root + "/runs/" + Segment(run.Id) + "/artifacts?per_page=100", null, token);
        return new(log, data.Required("artifacts").RequiredArray().Select(a => new ArtifactReference(Text(a, "id"), Text(a, "name"), Text(a, "archive_download_url"))).ToImmutableArray());
    }
}
