using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nightshift.Core;
using static Nightshift.Adapters.ServiceHttp;

namespace Nightshift.Adapters;

// Origin: https://dev.azure.com/{organization}/{project}/ . Services API 7.1 only.
public sealed class AzureRepository(ServiceHttp http, RepositoryIdentity repository) : IRepositoryService
{
    public string Name => "azure-devops";
    private string Root => "_apis/git/repositories/" + Segment(repository.Name);
    public async Task<PullRequestIdentity?> FindPullRequestAsync(Candidate candidate, string correlation, CancellationToken token)
    {
        Correlate(candidate.Repository == repository);
        var data = await http.Json(HttpMethod.Get, Root + "/pullrequests?searchCriteria.status=all&$top=100&api-version=7.1", null, token);
        var matches = data.GetProperty("value").EnumerateArray().Where(p => Text(p, "description").Contains("[nightshift:" + correlation + "]", StringComparison.Ordinal)
            && Text(p.GetProperty("lastMergeSourceCommit"), "commitId") == candidate.Revision).ToArray();
        if (matches.Length > 1) throw new ServiceException(ServiceFailure.Uncertain, "Multiple correlated pull requests");
        return matches.Length == 0 ? null : Map(matches[0], candidate, correlation);
    }
    public async Task<PullRequestIdentity> CreatePullRequestAsync(Candidate candidate, string sourceRef, string targetRef, string correlation, CancellationToken token)
    {
        Correlate(candidate.Repository == repository);
        var refs = await http.Json(HttpMethod.Get, Root + "/refs?filter=" + Segment("heads/" + sourceRef) + "&api-version=7.1", null, token);
        Correlate(refs.GetProperty("value").EnumerateArray().Any(r => Text(r, "name") == "refs/heads/" + sourceRef && Text(r, "objectId") == candidate.Revision));
        var data = await http.Json(HttpMethod.Post, Root + "/pullrequests?api-version=7.1", new { title = "Nightshift candidate " + candidate.Revision,
            sourceRefName = "refs/heads/" + sourceRef, targetRefName = "refs/heads/" + targetRef, description = "[nightshift:" + correlation + "]" }, token);
        return Map(data, candidate, correlation);
    }
    private PullRequestIdentity Map(JsonElement data, Candidate candidate, string correlation)
    {
        Correlate(Text(data.GetProperty("lastMergeSourceCommit"), "commitId") == candidate.Revision
            && (Text(data.GetProperty("repository"), "id") == repository.Name || Text(data.GetProperty("repository"), "name") == repository.Name)
            && Text(data, "description").Contains("[nightshift:" + correlation + "]", StringComparison.Ordinal));
        return new(Text(data, "pullRequestId"), Text(data.GetProperty("_links").GetProperty("web"), "href"), candidate.Revision, correlation);
    }
}
public sealed class AzurePipelines(ServiceHttp http, RepositoryIdentity repository) : IPipelineService
{
    public string Name => "azure-pipelines";
    private async Task Approved(Candidate candidate, PipelineDefinition pipeline, CancellationToken token)
    {
        Correlate(candidate.Repository == repository);
        var definition = await http.Json(HttpMethod.Get, "_apis/build/definitions/" + Segment(pipeline.Id) + "?api-version=7.1", null, token);
        Correlate(Text(definition, "id") == pipeline.Id && (Text(definition.GetProperty("repository"), "id") == repository.Name || Text(definition.GetProperty("repository"), "name") == repository.Name));
        string yamlPath = Text(definition.GetProperty("process"), "yamlFilename");
        var item = await http.Json(HttpMethod.Get, "_apis/git/repositories/" + Segment(repository.Name) + "/items?path=" + Segment(yamlPath)
            + "&includeContent=true&versionDescriptor.versionType=commit&versionDescriptor.version=" + Segment(candidate.Revision) + "&api-version=7.1", null, token);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(item, "content"))));
        Correlate(pipeline.Revision == Text(definition, "revision") + ":" + digest);
    }
    public async Task<PipelineRun?> RequestAsync(Candidate candidate, PipelineDefinition pipeline, string correlation, CancellationToken token)
    {
        await Approved(candidate, pipeline, token);
        var properties = new Dictionary<string, string> { ["nightshift.correlation"] = correlation };
        var data = await http.Json(HttpMethod.Post, "_apis/build/builds?api-version=7.1", new { definition = new { id = int.Parse(pipeline.Id) },
            sourceVersion = candidate.Revision, sourceBranch = pipeline.Ref, properties }, token);
        return Map(data, candidate, pipeline, correlation);
    }
    public async Task<PipelineRun?> ReconcileAsync(Candidate candidate, PipelineDefinition pipeline, string correlation, CancellationToken token)
    {
        await Approved(candidate, pipeline, token);
        var data = await http.Json(HttpMethod.Get, "_apis/build/builds?definitions=" + Segment(pipeline.Id)
            + "&branchName=" + Segment(pipeline.Ref) + "&properties=nightshift.correlation&$top=100&api-version=7.1", null, token);
        var matches = data.GetProperty("value").EnumerateArray().Where(b => Text(b, "sourceVersion") == candidate.Revision && Correlation(b) == correlation).ToArray();
        if (matches.Length > 1) throw new ServiceException(ServiceFailure.Uncertain, "Multiple correlated builds");
        return matches.Length == 0 ? null : Map(matches[0], candidate, pipeline, correlation);
    }
    private static string Correlation(JsonElement data) => data.TryGetProperty("properties", out var properties) ? Text(properties, "nightshift.correlation") : "";
    private PipelineRun Map(JsonElement data, Candidate candidate, PipelineDefinition pipeline, string correlation)
    {
        Correlate(Text(data, "sourceVersion") == candidate.Revision && Text(data.GetProperty("definition"), "id") == pipeline.Id
            && Text(data, "sourceBranch") == pipeline.Ref && Correlation(data) == correlation
            && (Text(data.GetProperty("repository"), "id") == repository.Name || Text(data.GetProperty("repository"), "name") == repository.Name));
        string status = Text(data, "status"), result = Text(data, "result");
        var state = status switch { "notStarted" or "postponed" => CheckState.Pending, "inProgress" or "cancelling" => CheckState.Running,
            "completed" => result switch { "succeeded" => CheckState.Passed, "failed" or "partiallySucceeded" => CheckState.Failed, "canceled" => CheckState.Cancelled, _ => CheckState.Unknown }, _ => CheckState.Unknown };
        return new(Text(data, "id"), Text(data.GetProperty("_links").GetProperty("web"), "href"), candidate.Revision, pipeline.Id, correlation, state);
    }
    public async Task<PipelineRun> ObserveAsync(Candidate candidate, PipelineDefinition pipeline, PipelineRun run, CancellationToken token)
    {
        await Approved(candidate, pipeline, token);
        var data = await http.Json(HttpMethod.Get, "_apis/build/builds/" + Segment(run.Id) + "?propertyFilters=nightshift.correlation&api-version=7.1", null, token);
        var mapped = Map(data, candidate, pipeline, run.Correlation); Correlate(mapped.Id == run.Id); return mapped;
    }
    public async Task CancelAsync(PipelineRun run, CancellationToken token)
    {
        await http.Json(HttpMethod.Patch, "_apis/build/builds/" + Segment(run.Id) + "?api-version=7.1", new { status = "cancelling" }, token);
    }
    public async Task<PipelineOutput> OutputAsync(PipelineRun run, CancellationToken token)
    {
        string root = "_apis/build/builds/" + Segment(run.Id);
        var logs = await http.Json(HttpMethod.Get, root + "/logs?api-version=7.1", null, token);
        var text = new StringBuilder();
        foreach (var log in logs.GetProperty("value").EnumerateArray().Take(16))
        {
            text.Append(await http.Logs(root + "/logs/" + Segment(Text(log, "id")) + "?api-version=7.1", false, token));
            if (text.Length > 1024 * 1024) throw new ServiceException(ServiceFailure.Protocol, "Combined log bounds exceeded");
        }
        var artifacts = await http.Json(HttpMethod.Get, root + "/artifacts?api-version=7.1", null, token);
        return new(text.ToString(), artifacts.GetProperty("value").EnumerateArray().Select(a => new ArtifactReference(Text(a, "id"), Text(a, "name"), Text(a.GetProperty("resource"), "downloadUrl"))).ToImmutableArray());
    }
}
