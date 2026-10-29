using System.Collections.Immutable;
using System.Formats.Tar;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Nightshift.Adapters;
using Nightshift.Cli;
using Nightshift.Core;

namespace Nightshift.Tests;

public class TrustedContractTests
{
    private sealed class ResultLauncher(WorkerResult result) : IWorkerLauncher
    {
        public int Calls { get; private set; }
        public Task<RuntimeIdentity> VerifyAsync(CancellationToken token) => Task.FromResult(new RuntimeIdentity("test", "1", "none", "none", "test"));
        public Task<WorkerResult> ExecuteAsync(WorkerCommand command, CancellationToken token) { Calls++; token.ThrowIfCancellationRequested(); return Task.FromResult(result); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Immutable_agent_contract_rejects_unsupported_features_before_launch(bool claude)
    {
        var launcher = new ResultLauncher(new(0, "", "", "fixture"));
        ICodingAgent agent = claude ? new ClaudeCodeAgent(launcher) : new CodexAgent(launcher);
        foreach (var task in new[] { new AgentTask("task", "context", "compact"), new("task", "context", SessionRequested: true), new("task", "context", SteeringRequested: true), new("task", "context", HooksRequested: true) })
            Assert.Equal(AgentResultKind.ConfigurationError, (await agent.ExecuteAsync(task, CancellationToken.None)).Kind);
        Assert.Equal(0, launcher.Calls);
        Assert.False(agent.Capabilities.Sessions); Assert.False(agent.Capabilities.Hooks); Assert.False(agent.Capabilities.Steering);
    }
    [Theory]
    [InlineData(false, "{bad", 0, AgentResultKind.Incomplete)]
    [InlineData(true, "{bad", 0, AgentResultKind.Incomplete)]
    [InlineData(false, "{\"type\":\"turn.started\"}", 0, AgentResultKind.Incomplete)]
    [InlineData(true, "{\"type\":\"assistant\"}", 0, AgentResultKind.Incomplete)]
    [InlineData(false, "{\"type\":\"error\"}", 0, AgentResultKind.TaskFailure)]
    [InlineData(true, "{\"type\":\"result\",\"is_error\":true,\"subtype\":\"success\"}", 0, AgentResultKind.TaskFailure)]
    [InlineData(false, "", 4, AgentResultKind.WorkerFailure)]
    [InlineData(true, "", 4, AgentResultKind.WorkerFailure)]
    [InlineData(false, "{}", 0, AgentResultKind.Incomplete)]
    [InlineData(true, "{}", 0, AgentResultKind.Incomplete)]
    public async Task Agent_protocol_rejects_malformed_partial_nonzero_and_zero_exit_errors(bool claude, string stdout, int exit, AgentResultKind expected)
    {
        var launcher = new ResultLauncher(new(exit, stdout, "dummy", "fixture"));
        ICodingAgent agent = claude ? new ClaudeCodeAgent(launcher) : new CodexAgent(launcher);
        Assert.Equal(expected, (await agent.ExecuteAsync(new("task", "approved context"), CancellationToken.None)).Kind);
    }
    [Fact]
    public void Acceptance_requires_exact_provenance_candidate_policy_check_and_terminal_pass()
    {
        var c = new Candidate(new("fixture", "owner", "repo"), new string('a', 40), "digest");
        var valid = new VerificationEvidence(EvidenceSource.ControlledLocalCheck, c, "policy", "check", CheckState.Passed, "owned-unit", DateTimeOffset.UtcNow);
        Assert.True(Acceptance.Decide(c, "policy", ["check"], [valid]).Accepted);
        foreach (var source in new[] { EvidenceSource.WorkerClaim, EvidenceSource.HumanDecision }) Assert.False(Acceptance.Decide(c, "policy", ["check"], [valid with { Source = source }]).Accepted);
        foreach (var status in new[] { CheckState.Pending, CheckState.Running, CheckState.Failed, CheckState.Cancelled, CheckState.Unknown }) Assert.False(Acceptance.Decide(c, "policy", ["check"], [valid with { State = status }]).Accepted);
        Assert.False(Acceptance.Decide(c with { SnapshotDigest = "changed" }, "policy", ["check"], [valid]).Accepted);
        Assert.False(Acceptance.Decide(c with { Revision = new string('b', 40) }, "policy", ["check"], [valid]).Accepted);
        Assert.False(Acceptance.Decide(c, "changed-policy", ["check"], [valid]).Accepted);
        Assert.False(Acceptance.Decide(c, "policy", ["other-check"], [valid]).Accepted);
        Assert.False(Acceptance.Decide(c, "policy", [], [valid]).Accepted);
        Assert.False(Acceptance.Decide(c, "policy", ["check"], []).Accepted);
    }
    [Theory]
    [InlineData("../checkpoint.json", TarEntryType.RegularFile)]
    [InlineData("/absolute", TarEntryType.RegularFile)]
    [InlineData("safe", TarEntryType.SymbolicLink)]
    [InlineData("safe", TarEntryType.HardLink)]
    [InlineData("safe", TarEntryType.Fifo)]
    [InlineData(".git/config", TarEntryType.RegularFile)]
    public void Archive_never_extracts_traversal_links_special_files_or_git_administration(string path, TarEntryType type)
    {
        using var bytes = new MemoryStream();
        using (var writer = new TarWriter(bytes, true))
        {
            var entry = new PaxTarEntry(type, path);
            if (type is TarEntryType.SymbolicLink or TarEntryType.HardLink) entry.LinkName = "/protected/checkpoint.json";
            if (type == TarEntryType.RegularFile) entry.DataStream = new MemoryStream("forged"u8.ToArray());
            writer.WriteEntry(entry);
        }
        Assert.Throws<IOException>(() => WorkspaceSnapshot.ReadArchive(bytes.ToArray()));
    }
    [Fact]
    public void Run_store_rejects_stage_path_escape_and_state_roots_reject_checkout_or_share()
    {
        using var w = new TestWorkspace(); using var store = new RunStore(w.RunDirectory());
        Assert.Throws<IOException>(() => store.StageDirectory("../escape", 1, 1));
        Assert.Throws<IOException>(() => new ProtectedState(w.Root, Path.Combine(w.Root, "state")));
        Assert.Throws<IOException>(() => new ProtectedState(w.Root, ContainerProfile.LocalDefault.ShareRoot));
    }
    [Fact]
    public async Task Protected_cli_fails_closed_without_adoption_and_does_not_accept_project_state_redirection()
    {
        using var w = new TestWorkspace(); string state = Path.Combine(Path.GetDirectoryName(w.Root)!, Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(w.Root, "nightshift.json"), "{\"run_output_root\":\"/tmp/untrusted\",\"backend\":\"scripted\"}");
            var output = new StringWriter(); var error = new StringWriter();
            int code = await new CliApplication(output, error, w.Root).RunAsync(["run", "--state-root", state]);
            Assert.Equal(2, code); Assert.Contains("No approved protected policy", error.ToString());
            Assert.False(Directory.Exists(Path.Combine(w.Root, ".nightshift")));
        }
        finally { if (Directory.Exists(state)) Directory.Delete(state, true); }
    }
}

public class EvidenceAndGitTests
{
    [Fact]
    public async Task Evidence_order_survives_wall_clock_reversal_and_later_failure_overrides_prior_pass()
    {
        using var w = new TestWorkspace();
        var candidate = new Candidate(new("fixture", "owner", "repo"), "commit", "snapshot");
        var passed = new VerificationEvidence(EvidenceSource.ControlledLocalCheck, candidate, "policy", "check", CheckState.Passed, "unit1", DateTimeOffset.UtcNow);
        await EvidenceLedger.AppendAsync(w.Root, passed, CancellationToken.None);
        await EvidenceLedger.AppendAsync(w.Root, passed with { State = CheckState.Failed, ExecutionId = "unit2", RecordedAt = passed.RecordedAt.AddHours(-1) }, CancellationToken.None);
        var records = EvidenceLedger.Read(w.Root); Assert.Equal(CheckState.Failed, records[^1].State);
        Assert.False(Acceptance.Decide(candidate, "policy", ["check"], records).Accepted);
    }
    [Fact]
    public async Task Local_git_commit_contains_exact_snapshot_bytes_despite_ignore_and_attributes()
    {
        using var w = new TestWorkspace();
        var snapshot = new WorkspaceSnapshot([new(".gitignore", "candidate.txt\n"u8.ToArray(), false), new(".gitattributes", "* text eol=lf\n"u8.ToArray(), false), new("candidate.txt", "CRLF\r\n"u8.ToArray(), false)]);
        string git = Path.Combine(w.Root, "private-git");
        var candidate = await new LocalGit().CommitSnapshotAsync(git, snapshot, new("fixture", "owner", "repo"), CancellationToken.None);
        var start = new System.Diagnostics.ProcessStartInfo("/usr/bin/git") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-C", git, "cat-file", "blob", candidate.Revision + ":candidate.txt" }) start.ArgumentList.Add(arg);
        using var p = System.Diagnostics.Process.Start(start)!; string actual = await p.StandardOutput.ReadToEndAsync(); await p.WaitForExitAsync();
        Assert.Equal(0, p.ExitCode); Assert.Equal("CRLF\r\n", actual); Assert.Equal(snapshot.Digest, candidate.SnapshotDigest);
    }
}
