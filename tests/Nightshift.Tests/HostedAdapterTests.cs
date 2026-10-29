using System.Net;
using System.Net.Http.Headers;
using Nightshift.Adapters;
using Nightshift.Core;

namespace Nightshift.Tests;

public class HostedAdapterTests
{
    private sealed class Fixture : IDisposable
    {
        internal readonly TestWorkspace Work = new();
        internal Candidate Candidate { get; } = new(new("fixture", "owner", "repo"), new string('a', 40), "snapshot-digest");
        internal PipelineDefinition Definition { get; }
        internal ReferenceFixture Transport { get; }
        internal ServiceHttp Http { get; }
        internal IRepositoryService Repository { get; }
        internal IPipelineService Pipeline { get; }
        internal HostedWorkflow Workflow { get; }
        internal Fixture(bool azureRepo, bool azurePipeline)
        {
            Transport = new(Work.Root, Candidate, azureRepo ? "azure-devops" : "github", azurePipeline ? "azure-pipelines" : "github-actions");
            Http = new(new Uri("https://fixture.invalid/"), () => new AuthenticationHeaderValue("Bearer", "dummy-fixture-secret"), "2022-11-28", Transport);
            Repository = azureRepo ? new AzureRepository(Http, Candidate.Repository) : new GitHubRepository(Http, Candidate.Repository);
            Pipeline = azurePipeline ? new AzurePipelines(Http, Candidate.Repository) : new GitHubActions(Http, Candidate.Repository);
            Definition = new("17", ReferenceFixture.PolicyRevision(azurePipeline), "refs/heads/candidate");
            Workflow = new(Repository, Pipeline);
        }
        internal Task<HostedIntent> Advance() => Workflow.AdvanceAsync(Path.Combine(Work.Root, "effects"), Candidate, Definition, "candidate", "main", CancellationToken.None);
        public void Dispose() { Http.Dispose(); Work.Dispose(); }
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task Repository_and_pipeline_selections_work_independently_and_reconcile_lost_post_response(bool azureRepo, bool azurePipeline)
    {
        using var f = new Fixture(azureRepo, azurePipeline); f.Transport.InterruptAfterPr = true;
        var error = await Assert.ThrowsAsync<ServiceException>(f.Advance); Assert.Equal(ServiceFailure.Transport, error.Kind);
        f.Transport.InterruptAfterDispatch = true;
        error = await Assert.ThrowsAsync<ServiceException>(f.Advance); Assert.Equal(ServiceFailure.Transport, error.Kind);
        var result = await f.Advance(); Assert.Equal(CheckState.Passed, result.Run!.State);
        Assert.Equal(1, f.Transport.PrPosts); Assert.Equal(1, f.Transport.CheckPosts);
        Assert.Equal("8", result.PullRequest!.Id); Assert.Equal("27", result.Run.Id);
        Assert.Equal(f.Candidate.Revision, result.Run.CandidateRevision);
        var output = await f.Pipeline.OutputAsync(result.Run, CancellationToken.None);
        Assert.Contains("fixture check passed", output.Log); Assert.Equal("31", Assert.Single(output.Artifacts).Id);
        var evidence = HostedWorkflow.Evidence(result, "policy"); Assert.True(Acceptance.Decide(f.Candidate, "policy", ["hosted:17"], [evidence]).Accepted);
        await f.Pipeline.CancelAsync(result.Run, CancellationToken.None);
        var cancelled = await f.Pipeline.ObserveAsync(f.Candidate, f.Definition, result.Run, CancellationToken.None); Assert.Equal(CheckState.Cancelled, cancelled.State);
        Assert.Equal(1, f.Transport.CheckPosts);
        Assert.Contains(f.Transport.Requests, r => r.Method == "POST" && r.Body.Contains(azurePipeline ? "sourceVersion" : "nightshift_candidate", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Transport.Requests, r => r.Body.Contains("dummy-fixture-secret", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(false, "queued", "", CheckState.Pending)] [InlineData(true, "queued", "", CheckState.Pending)]
    [InlineData(false, "in_progress", "", CheckState.Running)] [InlineData(true, "in_progress", "", CheckState.Running)]
    [InlineData(false, "completed", "neutral", CheckState.Unknown)] [InlineData(true, "completed", "none", CheckState.Unknown)]
    [InlineData(false, "completed", "cancelled", CheckState.Cancelled)] [InlineData(true, "completed", "cancelled", CheckState.Cancelled)]
    [InlineData(false, "completed", "failure", CheckState.Failed)] [InlineData(true, "completed", "failed", CheckState.Failed)]
    public async Task Native_states_are_mapped_conservatively(bool azure, string status, string result, CheckState expected)
    {
        using var f = new Fixture(azure, azure); f.Transport.NativeStatus = status; f.Transport.NativeResult = result;
        var intent = await f.Advance(); Assert.Equal(expected, intent.Run!.State);
        Assert.False(Acceptance.Decide(f.Candidate, "policy", ["hosted:17"], [HostedWorkflow.Evidence(intent, "policy")]).Accepted);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Changed_commit_definition_duplicate_correlation_and_ambiguous_dispatch_never_pass(bool azure)
    {
        using var f = new Fixture(azure, azure);
        var first = await f.Advance();
        f.Transport.CommitOverride = new string('b', 40);
        var wrong = await Assert.ThrowsAsync<ServiceException>(f.Advance); Assert.Equal(ServiceFailure.Protocol, wrong.Kind);
        f.Transport.CommitOverride = f.Candidate.Revision;
        f.Transport.YamlOverride = "agent changed verification YAML";
        var changed = await Assert.ThrowsAsync<ServiceException>(f.Advance); Assert.Equal(ServiceFailure.Protocol, changed.Kind);
        f.Transport.YamlOverride = null; f.Transport.DuplicateRuns = true;
        var duplicate = await Assert.ThrowsAsync<ServiceException>(() => f.Pipeline.ReconcileAsync(f.Candidate, f.Definition, first.Correlation, CancellationToken.None));
        Assert.Equal(ServiceFailure.Uncertain, duplicate.Kind);
        using var ambiguous = new Fixture(azure, azure); ambiguous.Transport.InterruptAfterDispatch = true;
        await Assert.ThrowsAsync<ServiceException>(ambiguous.Advance);
        ambiguous.Transport.HideReconciliation = true;
        var uncertain = await ambiguous.Advance(); Assert.StartsWith("uncertain:", uncertain.State); Assert.Null(uncertain.Run); Assert.Equal(1, ambiguous.Transport.CheckPosts);
        await ambiguous.Advance(); Assert.Equal(1, ambiguous.Transport.CheckPosts);
    }
    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized, ServiceFailure.Authentication)] [InlineData(true, HttpStatusCode.Unauthorized, ServiceFailure.Authentication)]
    [InlineData(false, HttpStatusCode.Forbidden, ServiceFailure.Authorization)] [InlineData(true, HttpStatusCode.Forbidden, ServiceFailure.Authorization)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, ServiceFailure.Transport)] [InlineData(true, HttpStatusCode.ServiceUnavailable, ServiceFailure.Transport)]
    public async Task Authentication_authorization_and_transport_errors_are_distinct_and_redacted(bool azure, HttpStatusCode status, ServiceFailure expected)
    {
        using var f = new Fixture(azure, azure); f.Transport.ErrorStatus = status;
        var error = await Assert.ThrowsAsync<ServiceException>(f.Advance); Assert.Equal(expected, error.Kind);
        Assert.DoesNotContain("secret", error.Message); Assert.DoesNotContain("dummy-fixture", error.Message);
    }
    [Fact]
    public async Task Credentials_are_never_forwarded_to_arbitrary_log_redirects_or_cross_origin_requests()
    {
        using var f = new Fixture(false, false); var intent = await f.Advance(); f.Transport.LogRedirect = "https://attacker.invalid/steal";
        var error = await Assert.ThrowsAsync<ServiceException>(() => f.Pipeline.OutputAsync(intent.Run!, CancellationToken.None));
        Assert.Equal(ServiceFailure.Protocol, error.Kind);
        Assert.Equal(ServiceFailure.Configuration, (await Assert.ThrowsAsync<ServiceException>(() => f.Http.Json(HttpMethod.Get, "https://attacker.invalid", null, CancellationToken.None))).Kind);
    }
}
