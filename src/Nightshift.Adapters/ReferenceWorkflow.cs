using System.Net.Http.Headers;
using System.Text;
using Nightshift.Core;

namespace Nightshift.Adapters;

public sealed record ReferenceResult(bool Accepted, bool StaleRejected, int Dispatches, int PullRequests, string Directory,
    string RepositoryService, string PipelineService, string Evidence, string Assurance = "Actual local isolation + offline HTTP fixtures; live accounts unexecuted");
public static class ReferenceWorkflow
{
    public const string FakeCodex = """
#!/bin/sh
if [ "$1" = --version ]; then echo 'fake-codex 1'; exit 0; fi
cat >/tmp/prompt
printf 'checked candidate\n' > candidate.txt
printf '%s\n' '{"type":"item.completed","item":{"type":"agent_message","text":"{\"outcome\":\"success\",\"notes\":\"fixture candidate\"}"}}' '{"type":"turn.completed"}'
""";
    public const string FakeClaude = """
#!/bin/sh
if [ "$1" = --version ]; then echo 'fake-claude 1'; exit 0; fi
cat >/tmp/prompt
printf 'checked candidate\n' > candidate.txt
printf '%s\n' '{"type":"result","subtype":"success","is_error":false,"structured_output":{"outcome":"success","notes":"fixture candidate"}}'
""";
    public static async Task<ReferenceResult> RunAsync(ProtectedState parent, string repositoryService, string pipelineService, CancellationToken token)
    {
        if (repositoryService is not "github" and not "azure-devops" || pipelineService is not "github-actions" and not "azure-pipelines") throw new ArgumentException("Reference supports github|azure-devops and github-actions|azure-pipelines independently");
        string project = Path.Combine(Path.GetDirectoryName(parent.Root)!, "reference-projects", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "fake-agent"), FakeCodex, token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(project, "fake-agent"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var state = new ProtectedState(project, parent.Root);
        var policy = new ApprovedPolicy("digraph reference { graph [default_fidelity=truncate]; start -> work -> exit; work [prompt=\"Produce fixture candidate\"]; }", "fake-codex", "/workspace/fake-agent", ContainerProfile.LocalDefault,
            [new("local-bytes", "printf 'checked candidate\\n' | cmp -s - candidate.txt")], [new("fake-agent", Convert.ToBase64String(Encoding.UTF8.GetBytes(FakeCodex)))], repositoryService, pipelineService);
        string revision = await state.AdoptAsync(policy, token); string run = state.NewRun();
        var workflow = new ProtectedWorkflow(state);
        var paused = await workflow.RunAsync(run, false, new(10, 60, 1), new QueueInterviewer([]), token);
        if (paused.Pipeline.State != "interrupted") throw new IOException("Reference did not pause");
        // Changing current defaults cannot change recorded backend/profile/policy on resume.
        await state.AdoptAsync(policy with { Backend = "scripted" }, token);
        var completed = await workflow.RunAsync(run, true, new(10, 60), new QueueInterviewer([]), token);
        if (!completed.Acceptance.Accepted) throw new IOException("Local reference acceptance failed: " + completed.Acceptance.Reason);
        var snapshot = ProtectedState.ReadSnapshot(run);
        var repo = new RepositoryIdentity("fixture", "owner", "repo");
        var candidate = await new LocalGit().CommitSnapshotAsync(Path.Combine(run, "git-candidate"), snapshot, repo, token);
        string resolved = await new LocalGit().ResolveRevisionAsync(Path.Combine(run, "git-candidate"), candidate.Revision, token);
        if (resolved != candidate.Revision) throw new IOException("Git candidate resolution mismatch");
        bool azurePipeline = pipelineService == "azure-pipelines";
        var definition = new PipelineDefinition("17", ReferenceFixture.PolicyRevision(azurePipeline), "refs/heads/candidate");
        var fixture = new ReferenceFixture(Path.Combine(run, "fixture"), candidate, repositoryService, pipelineService) { InterruptAfterDispatch = true };
        using var http = new ServiceHttp(new Uri("https://fixture.invalid/"), () => new AuthenticationHeaderValue("Bearer", "harmless-fixture-token"), "2022-11-28", fixture);
        IRepositoryService repository = repositoryService == "github" ? new GitHubRepository(http, repo) : new AzureRepository(http, repo);
        IPipelineService pipeline = azurePipeline ? new AzurePipelines(http, repo) : new GitHubActions(http, repo);
        var hosted = new HostedWorkflow(repository, pipeline);
        try { await hosted.AdvanceAsync(Path.Combine(run, "hosted"), candidate, definition, "candidate", "main", token); throw new IOException("Expected interrupted dispatch"); }
        catch (ServiceException e) when (e.Kind == ServiceFailure.Transport) { await File.WriteAllTextAsync(Path.Combine(run, "dispatch-interruption.txt"), e.Message, token); }
        var resumed = await hosted.AdvanceAsync(Path.Combine(run, "hosted"), candidate, definition, "candidate", "main", token);
        var evidence = HostedWorkflow.Evidence(resumed, revision);
        await AtomicFile.WriteJsonAsync(Path.Combine(run, "hosted-evidence.json"), evidence, token);
        var output = await pipeline.OutputAsync(resumed.Run!, token); await AtomicFile.WriteJsonAsync(Path.Combine(run, "hosted-output.json"), output, token);
        var accepted = Acceptance.Decide(candidate, revision, ["hosted:17"], [evidence]);
        var changed = new WorkspaceSnapshot(snapshot.Files.Append(new("later-change.txt", "new candidate"u8.ToArray(), false)));
        var stale = Acceptance.Decide(candidate with { Revision = "snapshot:" + changed.Digest, SnapshotDigest = changed.Digest }, revision, ["hosted:17"], [evidence]);
        await AtomicFile.WriteJsonAsync(Path.Combine(run, "stale-candidate.json"), new { previous = candidate, later_digest = changed.Digest, acceptance = stale }, token);
        await AtomicFile.WriteJsonAsync(Path.Combine(run, "fixture-requests.json"), fixture.Requests.Select(r => new { method = r.Method, path = r.Path, body = r.Body }), token);
        return new(accepted.Accepted, !stale.Accepted, fixture.CheckPosts, fixture.PrPosts, run, repositoryService, pipelineService, Path.Combine(run, "hosted-evidence.json"));
    }
}
