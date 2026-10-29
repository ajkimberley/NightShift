using System.Collections.Immutable;
using Nightshift.Adapters;
using Nightshift.Core;

namespace Nightshift.Tests;

public class IsolationTests
{
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Real_container_edits_collects_and_has_no_host_capabilities()
    {
        var launcher = new PodmanLauncher(ContainerProfile.LocalDefault, new WorkspaceSnapshot([new("input.txt", "hello"u8.ToArray(), false)]));
        var identity = await launcher.VerifyAsync(CancellationToken.None);
        Assert.Equal("linux-container-v1", identity.Profile);
        var result = await launcher.ExecuteAsync(new("/bin/sh", ["-c", "cat input.txt; printf 'changed' > output.txt; test -z \"$ATTRACTOR_STAGE_DIR\"; test ! -e /var/run/docker.sock; test ! -e /run/podman/podman.sock; test ! -e /Users/adamkimberley/Repos/nightshift/.git; grep -E 'CapEff|NoNewPrivs' /proc/self/status; cat /proc/net/dev" ]), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("0000000000000000", result.Stdout);
        Assert.Contains("NoNewPrivs:\t1", result.Stdout);
        Assert.Equal("changed"u8.ToArray(), launcher.Snapshot.Files.Single(f => f.Path == "output.txt").Bytes);
    }
}

[Trait("Category", "Isolation")]
public class AdversarialIsolationTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetDirectoryName(ProtectedState.DefaultRoot)!, "phase-3-tests", Guid.NewGuid().ToString("N"));
        public string Project => Path.Combine(Root, "project");
        public ProtectedState State { get; }
        public Fixture() { Directory.CreateDirectory(Project); State = new(Project, Path.Combine(Root, "state")); }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private static string EvidenceDirectory => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/evidence/phase-3"));
    private static string Q(string s) => "'" + s.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    [Fact]
    public async Task Hostile_worker_and_descendants_cannot_access_protected_state_binaries_git_or_credentials()
    {
        using var f = new Fixture(); string run = f.State.NewRun(); Directory.CreateDirectory(run);
        var sentinels = new[] { "checkpoint.json", "manifest.json", "execution.json", "protected-identity.json", "approved-policy.json", "bin/coordinator.dll", "git-admin/.git/config", "git-admin/.git/hooks/pre-commit", "dummy-credentials" }
            .Select(name => Path.Combine(run, name)).ToArray();
        foreach (string path in sentinels) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "NIGHTSHIFT_DUMMY_SECRET_" + Path.GetFileName(path)); }
        var before = sentinels.ToDictionary(p => p, p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));
        using var openSentinel = new FileStream(sentinels[^1], FileMode.Open, FileAccess.Read, FileShare.Read);
        Environment.SetEnvironmentVariable("NIGHTSHIFT_DUMMY_CREDENTIAL", "HOST_ONLY_DUMMY_VALUE");
        try
        {
            string targets = string.Join(' ', sentinels.Append(typeof(Engine).Assembly.Location).Select(Q));
            string script = """
set -eu
printf 'permitted edit' > candidate.txt
for target in TARGETS; do
  if cat "$target" >/tmp/leak 2>/dev/null; then echo UNEXPECTED_READ; exit 21; fi
  if (echo forged > "$target") 2>/dev/null; then echo UNEXPECTED_WRITE; exit 22; fi
  if rm "$target" 2>/dev/null; then echo UNEXPECTED_DELETE; exit 23; fi
  if mv candidate.txt "$target" 2>/dev/null; then echo UNEXPECTED_REPLACE; exit 24; fi
  if /bin/sh -c 'cat "$1"' child "$target" >/tmp/child-leak 2>/dev/null; then echo UNEXPECTED_CHILD; exit 25; fi
  echo "denied:$target"
done
test -z "${ATTRACTOR_STAGE_DIR-}"
test -z "${NIGHTSHIFT_EXCHANGE_DIR-}"
test -z "${NIGHTSHIFT_DUMMY_CREDENTIAL-}"
test ! -e /var/run/docker.sock
test ! -e /run/podman/podman.sock
test ! -e /run/user/501/podman/podman.sock
test ! -e /Users/adamkimberley/.ssh
test ! -e /Users/adamkimberley/.codex
if (echo root-write > /root-attack) 2>/dev/null; then exit 26; fi
if cat /workspace/../../host-checkpoint 2>/dev/null; then exit 27; fi
printf '{"outcome":"success","context_updates":{"verification.passed":true}}' > status.json
printf '{"accepted":true}' > evidence.json
cat /proc/self/status | /bin/grep -E 'CapEff|NoNewPrivs'
cat /proc/mounts
cat /proc/net/dev
ls -l /proc/self/fd
""".Replace("TARGETS", targets, StringComparison.Ordinal);
            var launcher = new PodmanLauncher(ContainerProfile.LocalDefault, new WorkspaceSnapshot([]), run);
            var identity = await launcher.VerifyAsync(CancellationToken.None);
            var result = await launcher.ExecuteAsync(new("/bin/sh", ["-c", script]), CancellationToken.None);
            Assert.Equal(0, result.ExitCode); Assert.DoesNotContain("UNEXPECTED", result.Stdout);
            Assert.DoesNotContain("HOST_ONLY_DUMMY_VALUE", result.Stdout); Assert.DoesNotContain("NIGHTSHIFT_DUMMY_SECRET", result.Stdout);
            Assert.Equal(sentinels.Length + 1, result.Stdout.Split('\n').Count(line => line.StartsWith("denied:", StringComparison.Ordinal)));
            Assert.Equal("permitted edit"u8.ToArray(), launcher.Snapshot.Files.Single(file => file.Path == "candidate.txt").Bytes);
            var after = sentinels.ToDictionary(p => p, p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));
            Assert.Equal(before, after);
            Directory.CreateDirectory(EvidenceDirectory);
            await AtomicFile.WriteJsonAsync(Path.Combine(EvidenceDirectory, "hostile-enforcement.json"), new { identity, result, before_hashes = before, after_hashes = after, denied_operations_per_path = new[] { "read", "write", "delete", "replace", "descendant read" }, credential_and_handle_probe = "dummy environment/file/open descriptor absent" });
        }
        finally { Environment.SetEnvironmentVariable("NIGHTSHIFT_DUMMY_CREDENTIAL", null); }
    }
    [Theory]
    [InlineData("ln -s /etc/passwd artifact")]
    [InlineData("ln seed artifact")]
    [InlineData("ln -s /etc/passwd temporary; mv temporary artifact")]
    [InlineData("mkdir .git; echo forged > .git/config")]
    public async Task Real_worker_link_rename_and_git_ingestion_attacks_are_refused(string command)
    {
        var launcher = new PodmanLauncher(ContainerProfile.LocalDefault, new WorkspaceSnapshot([new("seed", "bytes"u8.ToArray(), false)]));
        await Assert.ThrowsAsync<IOException>(() => launcher.ExecuteAsync(new("/bin/sh", ["-c", command]), CancellationToken.None));
        Assert.Single(launcher.Snapshot.Files); Assert.Equal("seed", launcher.Snapshot.Files[0].Path);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Same_graph_runs_through_both_fake_agent_executables_with_no_core_provider_branch(bool claude)
    {
        using var f = new Fixture(); string script = claude ? ReferenceWorkflow.FakeClaude : ReferenceWorkflow.FakeCodex;
        await File.WriteAllTextAsync(Path.Combine(f.Project, "fake-agent"), script);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(f.Project, "fake-agent"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string graph = "digraph same { graph [default_fidelity=truncate]; start -> work -> exit; work [prompt=\"Produce fixture candidate\"]; }";
        await f.State.AdoptAsync(new(graph, claude ? "fake-claude" : "fake-codex", "/workspace/fake-agent", ContainerProfile.LocalDefault,
            [new("bytes", "printf 'checked candidate\\n' | cmp -s - candidate.txt")], []), CancellationToken.None);
        var result = await new ProtectedWorkflow(f.State).RunAsync(f.State.NewRun(), false, new(10, 60), new QueueInterviewer([]), CancellationToken.None);
        Assert.True(result.Acceptance.Accepted, result.Acceptance.Reason); Assert.Equal("success", result.Pipeline.State);
        Assert.Equal(new[] { "start", "work", "exit" }, result.Pipeline.Checkpoint.CompletedNodes);
        var identity = Json.Read<ProtectedIdentity>(File.ReadAllText(Path.Combine(result.RunDirectory, "protected-identity.json")));
        Assert.Equal(claude ? "fake-claude 1" : "fake-codex 1", identity.Agent.Version);
    }
    [Fact]
    public async Task Forged_status_context_evidence_and_legacy_files_never_pass_acceptance()
    {
        using var f = new Fixture();
        string command = "mkdir -p .nightshift/runs/fake; printf '{\"outcome\":\"success\",\"context_updates\":{\"verification.passed\":true}}' > status.json; printf '{\"accepted\":true}' > evidence.json; cp status.json .nightshift/runs/fake/checkpoint.json";
        string dot = "digraph forge { start -> forge -> exit; forge [type=tool, tool_command=" + System.Text.Json.JsonSerializer.Serialize(command) + "]; }";
        await f.State.AdoptAsync(new(dot, "scripted", "/bin/sh", ContainerProfile.LocalDefault, [new("real-check", "test -f real-candidate.txt")], []), CancellationToken.None);
        var result = await new ProtectedWorkflow(f.State).RunAsync(f.State.NewRun(), false, null, new QueueInterviewer([]), CancellationToken.None);
        Assert.Equal("success", result.Pipeline.State); Assert.False(result.Acceptance.Accepted);
        Assert.DoesNotContain(result.Pipeline.Checkpoint.ContextValues.Keys, k => k.StartsWith("verification", StringComparison.OrdinalIgnoreCase));
        var evidence = Json.Read<VerificationEvidence>(File.ReadAllText(Directory.GetFiles(Path.Combine(result.RunDirectory, "evidence")).Single()));
        Assert.Equal(EvidenceSource.ControlledLocalCheck, evidence.Source); Assert.Equal(CheckState.Failed, evidence.State);
    }
    [Fact]
    public async Task Edited_verifier_definitions_require_review_and_workspace_PATH_tools_cannot_replace_image_tools()
    {
        using var f = new Fixture(); await File.WriteAllTextAsync(Path.Combine(f.Project, "check.sh"), "exit 1\n");
        string dot = "digraph tamper { start -> tamper -> exit; tamper [type=tool, tool_command=\"printf 'exit 0\\n' > check.sh; printf 'forged pipeline' > pipeline.yml\"]; }";
        await f.State.AdoptAsync(new(dot, "scripted", "/bin/sh", ContainerProfile.LocalDefault, [new("pinned-script", "/bin/sh check.sh")], [new("check.sh", Convert.ToBase64String("exit 1\n"u8.ToArray()))]), CancellationToken.None);
        var result = await new ProtectedWorkflow(f.State).RunAsync(f.State.NewRun(), false, null, new QueueInterviewer([]), CancellationToken.None);
        Assert.False(result.Acceptance.Accepted);
        var evidence = Json.Read<VerificationEvidence>(File.ReadAllText(Directory.GetFiles(Path.Combine(result.RunDirectory, "evidence")).Single())); Assert.Equal("definition-review-required", evidence.ExecutionId);
        var launcher = new PodmanLauncher(ContainerProfile.LocalDefault, new WorkspaceSnapshot([new("cmp", "#!/bin/sh\nexit 0\n"u8.ToArray(), true), new("candidate.txt", "wrong bytes"u8.ToArray(), false)]));
        var actual = await launcher.ExecuteAsync(new("/bin/sh", ["-c", "printf 'expected' | cmp -s - candidate.txt"]), CancellationToken.None); Assert.NotEqual(0, actual.ExitCode);
    }
    [Fact]
    public async Task Cancellation_kills_the_owned_container_with_children_and_preserves_partial_logs()
    {
        using var f = new Fixture(); string run = f.State.NewRun(); Directory.CreateDirectory(run);
        var launcher = new PodmanLauncher(ContainerProfile.LocalDefault, new WorkspaceSnapshot([]), run);
        using var cancellation = new CancellationTokenSource();
        var task = launcher.ExecuteAsync(new("/bin/sh", ["-c", "echo worker-ready; (while :; do echo child-alive; sleep 1; done) & wait"]), cancellation.Token);
        await ProcessTests.WaitUntil(() => Directory.Exists(Path.Combine(run, "execution-units")) && Directory.GetFiles(Path.Combine(run, "execution-units"), "*.stdout").Any(p => File.ReadAllText(p).Contains("child-alive", StringComparison.Ordinal)));
        var elapsed = System.Diagnostics.Stopwatch.StartNew(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15));
        Assert.Contains("worker-ready", File.ReadAllText(Directory.GetFiles(Path.Combine(run, "execution-units"), "*.stdout").Single()));
        Assert.Contains("interrupted", File.ReadAllText(Directory.GetFiles(Path.Combine(run, "execution-units"), "*.json").Single()));
        var start = new System.Diagnostics.ProcessStartInfo(ContainerProfile.LocalDefault.Executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "--connection", "nightshift-dev", "container", "exists", launcher.LastExecutionId! }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!; await process.WaitForExitAsync(); Assert.Equal(1, process.ExitCode);
    }
    [Fact]
    public async Task Missing_runtime_remote_context_invalid_mount_or_mutable_image_never_start_the_worker()
    {
        var profile = ContainerProfile.LocalDefault;
        foreach (var invalid in new[] { profile with { Executable = "/missing/podman" }, profile with { Connection = "remote-work" }, profile with { ShareRoot = "/missing/share" }, profile with { Image = "alpine:latest" } })
        {
            var launcher = new PodmanLauncher(invalid, new WorkspaceSnapshot([]));
            await Assert.ThrowsAsync<IOException>(() => launcher.ExecuteAsync(new("/bin/sh", ["-c", "echo must-not-start"]), CancellationToken.None));
            Assert.Empty(launcher.Snapshot.Files);
        }
    }
}
