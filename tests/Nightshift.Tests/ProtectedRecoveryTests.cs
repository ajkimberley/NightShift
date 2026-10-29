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
