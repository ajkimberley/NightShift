using System.Diagnostics;
using Nightshift.Adapters;
using Nightshift.Cli;
using Nightshift.Core;

namespace Nightshift.Tests;

[Trait("Category", "Isolation")]
public class ProtectedRecoveryTests
{
    [Fact]
    public async Task Abrupt_coordinator_crash_recovers_owned_execution_retains_attempts_and_accepts_replayed_candidate()
    {
        string root = Path.Combine(Path.GetDirectoryName(ProtectedState.DefaultRoot)!, "phase-3-tests", Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
        var state = new ProtectedState(project, Path.Combine(root, "state"));
        try
        {
            string graph = "digraph crash { start -> work -> exit; work [type=tool, tool_command=\"echo worker-ready; sleep 2; printf 'candidate' > candidate.txt\"]; }";
            await state.AdoptAsync(new(graph, "scripted", "/bin/sh", ContainerProfile.LocalDefault, [new("bytes", "printf 'candidate' | cmp -s - candidate.txt")], []), CancellationToken.None);
            string cli = typeof(CliApplication).Assembly.Location;
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = project, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { cli, "run", "--state-root", state.Root }) start.ArgumentList.Add(arg);
            string? run = null;
            using (var process = Process.Start(start)!)
            {
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    await ProcessTests.WaitUntil(() =>
                    {
                        string runs = Path.Combine(state.ProjectDirectory, "runs");
                        run = Directory.Exists(runs) ? Directory.GetDirectories(runs).SingleOrDefault() : null;
                        return run != null && Directory.Exists(Path.Combine(run, "execution-units"))
                            && Directory.GetFiles(Path.Combine(run, "execution-units"), "*.stdout").Any(p => File.ReadAllText(p).Contains("worker-ready", StringComparison.Ordinal));
                    });
                }
                finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); }
            }
            Assert.NotNull(run);
            string oldUnit = Directory.GetFiles(Path.Combine(run, "execution-units"), "*.json").Single();
            var before = Json.Read<Checkpoint>(File.ReadAllText(Path.Combine(run, "checkpoint.json"))); Assert.Equal("work", before.NextNode);
            var result = await new ProtectedWorkflow(state).RunAsync(run, true, null, new QueueInterviewer([]), CancellationToken.None);
            Assert.True(result.Acceptance.Accepted, result.Acceptance.Reason); Assert.Equal("success", result.Pipeline.State);
            var output = new StringWriter(); var error = new StringWriter();
            int inspectCode = await new CliApplication(output, error, root).RunAsync(["result", run, "--state-root", state.Root, "--json"]);
            Assert.True(inspectCode == 0, error.ToString()); Assert.Contains("protected coordinator record", output.ToString());
            Assert.Contains("interrupted", File.ReadAllText(oldUnit));
            Assert.Equal(2, Directory.GetDirectories(Path.Combine(run, "work", "visit-0001")).Length);
            Assert.True(Directory.GetFiles(Path.Combine(run, "checkpoints")).Length > before.Revision);
            string identityPath = Path.Combine(run, "protected-identity.json"); var identity = Json.Read<ProtectedIdentity>(File.ReadAllText(identityPath));
            await AtomicFile.WriteJsonAsync(identityPath, identity with { Runtime = identity.Runtime with { Profile = "weakened" } });
            await Assert.ThrowsAsync<IOException>(() => new ProtectedWorkflow(state).RunAsync(run, true, null, new QueueInterviewer([]), CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("github", "github-actions")]
    [InlineData("azure-devops", "azure-pipelines")]
    public async Task Full_reference_has_isolated_agent_exact_git_candidate_hosted_recovery_and_stale_rejection(string repository, string pipeline)
    {
        string root = Path.Combine(Path.GetDirectoryName(ProtectedState.DefaultRoot)!, "phase-3-reference-tests", Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
        try
        {
            var result = await ReferenceWorkflow.RunAsync(new(project, Path.Combine(root, "state")), repository, pipeline, CancellationToken.None);
            Assert.True(result.Accepted); Assert.True(result.StaleRejected); Assert.Equal(1, result.Dispatches); Assert.Equal(1, result.PullRequests);
            var evidence = Json.Read<VerificationEvidence>(File.ReadAllText(result.Evidence)); Assert.Equal(CheckState.Passed, evidence.State);
            Assert.Equal(EvidenceSource.HostedCi, evidence.Source); Assert.StartsWith(pipeline + ":", evidence.ExecutionId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

[Trait("Category", "Isolation")]
public class ProtectedDeadlineTests
{
    [Fact]
    public async Task Run_deadline_is_incomplete_resumable_and_can_resume_only_with_a_permitted_bound()
    {
        string root = Path.Combine(Path.GetDirectoryName(ProtectedState.DefaultRoot)!, "phase-3-deadline-tests", Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
        try
        {
            var state = new ProtectedState(project, Path.Combine(root, "state"));
            await state.AdoptAsync(new("digraph deadline { start -> work -> exit; work [type=tool, tool_command=\"echo ready; sleep 1; printf candidate > candidate.txt\"]; }", "scripted", "/bin/sh", ContainerProfile.LocalDefault,
                [new("bytes", "printf candidate | cmp -s - candidate.txt")], [], MaxSeconds: 30), CancellationToken.None);
            string run = state.NewRun(); var workflow = new ProtectedWorkflow(state);
            var timedOut = await workflow.RunAsync(run, false, new(10, .4), new QueueInterviewer([]), CancellationToken.None);
            Assert.Equal("interrupted", timedOut.Pipeline.State); Assert.False(timedOut.Acceptance.Accepted); Assert.Null(timedOut.Pipeline.Checkpoint.FinalOutcome);
            var resumed = await workflow.RunAsync(run, true, new(10, 20), new QueueInterviewer([]), CancellationToken.None);
            Assert.True(resumed.Acceptance.Accepted, resumed.Acceptance.Reason); Assert.Equal("success", resumed.Pipeline.State);
            Assert.True(Directory.GetFiles(Path.Combine(run, "execution-history")).Length > 0);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Hanging_verifier_is_bounded_cancelled_and_cannot_reuse_a_prior_passing_record()
    {
        string root = Path.Combine(Path.GetDirectoryName(ProtectedState.DefaultRoot)!, "phase-3-deadline-tests", Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
        try
        {
            var state = new ProtectedState(project, Path.Combine(root, "state"));
            await state.AdoptAsync(new("digraph verifier { start -> work -> exit; work [prompt=fixture]; }", "scripted", "/bin/sh", ContainerProfile.LocalDefault,
                [new("bounded", "echo verifier-ready; sleep 30", .5)], []), CancellationToken.None);
            var elapsed = Stopwatch.StartNew();
            var result = await new ProtectedWorkflow(state).RunAsync(state.NewRun(), false, null, new QueueInterviewer([]), CancellationToken.None);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15)); Assert.Equal("success", result.Pipeline.State); Assert.False(result.Acceptance.Accepted);
            var records = EvidenceLedger.Read(result.RunDirectory); Assert.Equal(CheckState.Pending, records[0].State); Assert.Equal(CheckState.Cancelled, records[^1].State);
            var output = new StringWriter(); var error = new StringWriter();
            int resumeCode = await new CliApplication(output, error, project).RunAsync(["resume", "--latest", "--state-root", state.Root]);
            Assert.True(resumeCode == 1, output.ToString() + error.ToString());
            Assert.True(EvidenceLedger.Read(result.RunDirectory).Count > records.Count);
            var oldPass = records[0] with { State = CheckState.Passed, RecordedAt = DateTimeOffset.UtcNow.AddDays(1) };
            Assert.False(Acceptance.Decide(records[0].Candidate, records[0].PolicyRevision, ["bounded"], new[] { oldPass }.Concat(records)).Accepted);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
