using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Nightshift.Core;

namespace Nightshift.Adapters;

// Explicit offline fixture transport. Never opens a socket or reads account credentials.
public sealed class ReferenceFixture(string directory, Candidate candidate, string repositoryService, string pipelineService) : HttpMessageHandler
{
    public const string Yaml = "name: approved-fixture\n# configured correlation input and immutable candidate required\n";
    public string CommitOverride { get; set; } = candidate.Revision;
    public string NativeStatus { get; set; } = "completed";
    public string NativeResult { get; set; } = "success";
    public string? YamlOverride { get; set; }
    public HttpStatusCode? ErrorStatus { get; set; }
    public bool InterruptAfterPr { get; set; }
    public bool InterruptAfterDispatch { get; set; }
    public bool HideReconciliation { get; set; }
    public bool DuplicateRuns { get; set; }
    public string LogRedirect { get; set; } = "";
    public List<(string Method, string Path, string Body)> Requests { get; } = [];
    private sealed record FixtureState(string Correlation = "", bool Pr = false, bool Run = false, int PrPosts = 0, int CheckPosts = 0);
    private FixtureState Load() => File.Exists(Path.Combine(directory, "fixture-state.json")) ? Json.Read<FixtureState>(File.ReadAllText(Path.Combine(directory, "fixture-state.json"))) : new();
    public int CheckPosts => Load().CheckPosts;
    public int PrPosts => Load().PrPosts;
    public static string PolicyRevision(bool azure) => (azure ? "3" : "17") + ":" + Engine.Hash(Yaml);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        string path = request.RequestUri!.AbsolutePath, body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token);
        Requests.Add((request.Method.Method, path + request.RequestUri.Query, body));
        if (ErrorStatus != null) return new(ErrorStatus.Value) { Content = new StringContent("dummy secret must never appear in diagnostics") };
        var state = Load(); bool azureRepo = repositoryService == "azure-devops", azurePipeline = pipelineService == "azure-pipelines";
        object Pr() => azureRepo ? new { pullRequestId = 8, description = "[nightshift:" + state.Correlation + "]", lastMergeSourceCommit = new { commitId = CommitOverride }, repository = new { id = candidate.Repository.Name, name = candidate.Repository.Name }, _links = new { web = new { href = "https://fixture.invalid/pr/8" } } }
            : new { number = 8, body = "[nightshift:" + state.Correlation + "]", head = new { sha = CommitOverride, repo = new { full_name = candidate.Repository.Namespace + "/" + candidate.Repository.Name } }, html_url = "https://fixture.invalid/pr/8" };
        object Run() => azurePipeline ? new { id = 27, sourceVersion = CommitOverride, sourceBranch = "refs/heads/candidate", definition = new { id = 17 }, repository = new { id = candidate.Repository.Name, name = candidate.Repository.Name }, properties = new Dictionary<string, string> { ["nightshift.correlation"] = state.Correlation }, status = NativeStatus == "queued" ? "notStarted" : NativeStatus == "in_progress" ? "inProgress" : NativeStatus, result = NativeResult == "success" ? "succeeded" : NativeResult == "cancelled" ? "canceled" : NativeResult, _links = new { web = new { href = "https://fixture.invalid/run/27" } } }
            : new { id = 27, head_sha = CommitOverride, head_branch = "candidate", workflow_id = 17, display_title = "nightshift-" + state.Correlation, @event = "workflow_dispatch", repository = new { full_name = candidate.Repository.Namespace + "/" + candidate.Repository.Name }, status = NativeStatus, conclusion = NativeResult, html_url = "https://fixture.invalid/run/27" };
        HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        async Task Persist() => await AtomicFile.WriteJsonAsync(Path.Combine(directory, "fixture-state.json"), state, token);
        if (request.Method == HttpMethod.Post && (path.EndsWith("/pulls", StringComparison.Ordinal) || path.EndsWith("/pullrequests", StringComparison.Ordinal)))
        {
            using var data = JsonDocument.Parse(body); string marker = data.RootElement.GetProperty(azureRepo ? "description" : "body").GetString()!;
            state = state with { Correlation = marker[12..^1], Pr = true, PrPosts = state.PrPosts + 1 }; await Persist();
            if (InterruptAfterPr) { InterruptAfterPr = false; throw new HttpRequestException("simulated response loss after PR creation"); }
            return Reply(Pr());
        }
        if (path.Contains("/pulls", StringComparison.Ordinal) || path.Contains("/pullrequests", StringComparison.Ordinal))
            return azureRepo ? Reply(new { value = state.Pr && !HideReconciliation ? new[] { Pr() } : [] }) : Reply(state.Pr && !HideReconciliation ? new[] { Pr() } : []);
        if (path.Contains("/git/ref/heads/", StringComparison.Ordinal)) return Reply(new { @object = new { sha = CommitOverride } });
        if (path.EndsWith("/refs", StringComparison.Ordinal)) return Reply(new { value = new[] { new { name = "refs/heads/candidate", objectId = CommitOverride } } });
        if (path.Contains("/contents/", StringComparison.Ordinal)) return Reply(new { encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(YamlOverride ?? Yaml)) });
        if (path.EndsWith("/items", StringComparison.Ordinal)) return Reply(new { content = YamlOverride ?? Yaml });
        if (path.Contains("/definitions/", StringComparison.Ordinal)) return Reply(new { id = 17, revision = 3, repository = new { id = candidate.Repository.Name }, process = new { yamlFilename = "azure-pipelines.yml" } });
        if (path.EndsWith("/workflows/17", StringComparison.Ordinal)) return Reply(new { id = 17, path = ".github/workflows/check.yml" });
        if (request.Method == HttpMethod.Post && (path.EndsWith("/dispatches", StringComparison.Ordinal) || path.EndsWith("/builds", StringComparison.Ordinal)))
        {
            using var data = JsonDocument.Parse(body);
            string correlation = azurePipeline ? data.RootElement.GetProperty("properties").GetProperty("nightshift.correlation").GetString()!
                : data.RootElement.GetProperty("inputs").GetProperty("nightshift_correlation").GetString()!;
            state = state with { Correlation = correlation, Run = true, CheckPosts = state.CheckPosts + 1 }; await Persist();
            if (InterruptAfterDispatch) { InterruptAfterDispatch = false; throw new HttpRequestException("simulated interruption after dispatch"); }
            return azurePipeline ? Reply(Run()) : new(HttpStatusCode.NoContent);
        }
        if (path.EndsWith("/cancel", StringComparison.Ordinal) || request.Method == HttpMethod.Patch)
        { NativeStatus = "completed"; NativeResult = "cancelled"; return request.Method == HttpMethod.Patch ? Reply(Run()) : new(HttpStatusCode.Accepted); }
        if (path.EndsWith("/artifacts", StringComparison.Ordinal)) return azurePipeline
            ? Reply(new { value = new[] { new { id = 31, name = "report", resource = new { downloadUrl = "https://fixture.invalid/artifact/31" } } } })
            : Reply(new { artifacts = new[] { new { id = 31, name = "report", archive_download_url = "https://fixture.invalid/artifact/31" } } });
        if (path.EndsWith("/logs/1", StringComparison.Ordinal)) return new(HttpStatusCode.OK) { Content = new StringContent("fixture check passed\n") };
        if (path.EndsWith("/logs", StringComparison.Ordinal))
        {
            if (LogRedirect.Length > 0) { var redirect = new HttpResponseMessage(HttpStatusCode.Found); redirect.Headers.Location = new Uri(LogRedirect); return redirect; }
            if (azurePipeline) return Reply(new { value = new[] { new { id = 1 } } });
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(zip.CreateEntry("check.txt").Open())) writer.WriteLine("fixture check passed");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.ToArray()) };
        }
        if (path.EndsWith("/runs/27", StringComparison.Ordinal) || path.EndsWith("/builds/27", StringComparison.Ordinal)) return Reply(Run());
        if (path.EndsWith("/runs", StringComparison.Ordinal) || path.EndsWith("/builds", StringComparison.Ordinal))
        {
            object[] runs = state.Run && !HideReconciliation ? DuplicateRuns ? [Run(), Run()] : [Run()] : [];
            return azurePipeline ? Reply(new { value = runs }) : Reply(new { workflow_runs = runs });
        }
        return new(HttpStatusCode.NotFound) { Content = new StringContent("Unimplemented fixture route") };
    }
}
