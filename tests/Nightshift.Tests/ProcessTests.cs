using System.Diagnostics;
using Nightshift.Core;

namespace Nightshift.Tests;

public class ProcessTests
{
    [Fact]
    public async Task ToolCommandCapturesOutputAndNonzeroOverridesAClaimedSuccess()
    {
        using var w = new TestWorkspace();
        var engine = new Engine(EngineTests.Registry(w.Root));
        var ok = await engine.RunAsync(EngineTests.StageGraph(", type=tool, tool_command=\"printf 'hello'; printf 'warning' >&2\""), w.RunDirectory(), w.Root);
        Assert.True(ok.Outcome.IsSuccess); Assert.Equal("hello", ok.Checkpoint.ContextValues["tool.output"].GetString());
        var fail = await engine.RunAsync(EngineTests.StageGraph(", type=tool, tool_command=\"printf '{\\\"outcome\\\":\\\"success\\\"}' > \\\"$ATTRACTOR_STAGE_DIR/status.json\\\"; exit 7\""), w.RunDirectory("fail"), w.Root);
        Assert.False(fail.Outcome.IsSuccess); Assert.Contains("exited 7", fail.Outcome.FailureReason);
        string workerStatus = Directory.GetFiles(w.RunDirectory("fail"), "worker-status.json", SearchOption.AllDirectories).Single();
        Assert.Equal(StageStatus.Success, Json.Read<Outcome>(await File.ReadAllTextAsync(workerStatus)).Status);
    }
    [Fact]
    public async Task ToolCanReturnExplicitStatusAndContextUpdates()
    {
        using var w = new TestWorkspace();
        string command = "printf '{\\\"outcome\\\":\\\"fail\\\",\\\"failure_reason\\\":\\\"business check failed\\\"}' > \\\"$ATTRACTOR_STAGE_DIR/status.json\\\"";
        var r = await new Engine(EngineTests.Registry(w.Root)).RunAsync(EngineTests.StageGraph($", type=tool, tool_command=\"{command}\""), w.RunDirectory(), w.Root);
        Assert.False(r.Outcome.IsSuccess); Assert.Equal("business check failed", r.Outcome.FailureReason);
    }
    [Fact]
    public async Task ProcessTimeoutKillsTheChildAndPreservesOutput()
    {
        using var w = new TestWorkspace(); string path = w.RunDirectory(); Directory.CreateDirectory(path);
        using var cancel = new CancellationTokenSource();
        var task = ProcessRunner.RunAsync("/bin/sh", ["-c", "sleep 30 & child=$!; echo $child > child.pid; echo ready; wait"], w.Root, "", path, cancel.Token);
        string childFile = Path.Combine(w.Root, "child.pid");
        await WaitForFile(childFile); int pid = int.Parse(await File.ReadAllTextAsync(childFile));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Contains("ready", await File.ReadAllTextAsync(Path.Combine(path, "stdout.txt")));
        await WaitUntil(() => !IsRunning(pid)); Assert.False(IsRunning(pid));
    }
    [Fact]
    public async Task StageTimeoutIsTransientAndExhaustsIntoFail()
    {
        using var w = new TestWorkspace();
        var r = await new Engine(EngineTests.Registry(w.Root), new NoWait()).RunAsync(EngineTests.StageGraph(", type=tool, tool_command=\"sleep 30\", timeout=50ms, max_retries=1"), w.RunDirectory(), w.Root);
        Assert.False(r.Outcome.IsSuccess); Assert.Contains("Max retries exceeded", r.Outcome.FailureReason);
        Assert.Equal(2, Directory.GetFiles(w.RunDirectory(), "process.pid", SearchOption.AllDirectories).Length);
    }
    internal static async Task<string> FakeCodex(TestWorkspace w, string eventJson = "{\"type\":\"turn.completed\"}", string outcome = "{\"outcome\":\"success\",\"notes\":\"fake claim\"}", int exit = 0)
    {
        string path = Path.Combine(w.Root, "fake-codex");
        string script = """
        #!/bin/sh
        printf '%s\n' "$@" > "$ATTRACTOR_STAGE_DIR/arguments.txt"
        output=''
        while [ "$#" -gt 0 ]; do
          if [ "$1" = '--output-last-message' ]; then shift; output=$1; fi
          shift
        done
        cat > "$ATTRACTOR_STAGE_DIR/input.txt"
        """ + $"\nprintf '%s\\n' '{eventJson}'\n" + (outcome.Length > 0 ? $"printf '%s' '{outcome}' > \"$output\"\n" : "") + $"exit {exit}\n";
        await File.WriteAllTextAsync(path, script);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    [Fact]
    public async Task OptInCodexProcessUsesDocumentedArgumentsStructuredOutcomeAndMinimalFidelity()
    {
        using var w = new TestWorkspace(); string fake = await FakeCodex(w);
        var b = new CodexProcessBackend(new(true, fake, w.Root));
        var r = await new Engine(EngineTests.Registry(w.Root, b)).RunAsync(EngineTests.StageGraph(", fidelity=truncate, llm_model=test-model, reasoning_effort=low"), w.RunDirectory(), w.Root);
        Assert.True(r.Outcome.IsSuccess); Assert.Contains("Worker claim", r.Checkpoint.NodeOutcomes["work"].Notes);
        string argFile = Directory.GetFiles(w.RunDirectory(), "arguments.txt", SearchOption.AllDirectories).Single(); string args = await File.ReadAllTextAsync(argFile);
        Assert.Contains("exec\n--json\n--ephemeral\n--sandbox\nread-only", args); Assert.Contains("--output-schema", args); Assert.Contains("--output-last-message", args); Assert.Contains("test-model", args);
        Assert.DoesNotContain("danger", args); Assert.DoesNotContain("yolo", args);
        string schemaPath = Directory.GetFiles(w.RunDirectory(), "outcome-schema.json", SearchOption.AllDirectories).Single();
        using var schema = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(schemaPath));
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        Assert.Equal(4, schema.RootElement.GetProperty("properties").GetProperty("outcome").GetProperty("enum").GetArrayLength());
        string configPath = Directory.GetFiles(w.RunDirectory(), "configuration.json", SearchOption.AllDirectories).Single();
        using var config = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        Assert.Equal("test-model", config.RootElement.GetProperty("model").GetString());
        Assert.Equal("truncate", config.RootElement.GetProperty("handoff").GetProperty("fidelity").GetString());
        Assert.False(b.Capabilities.SessionReuse); Assert.Equal(new[] { "truncate" }, b.Capabilities.FidelityModes);
    }
    [Theory]
    [InlineData("{\"type\":\"turn.failed\"}", "{\"outcome\":\"success\"}", 0, "failure")]
    [InlineData("{\"type\":\"thread.started\"}", "{\"outcome\":\"success\"}", 0, "incomplete")]
    [InlineData("bad-json", "{\"outcome\":\"success\"}", 0, "Malformed")]
    [InlineData("{\"type\":\"turn.completed\"}", "", 0, "without structured")]
    [InlineData("{\"type\":\"turn.completed\"}", "{\"notes\":\"fluent\"}", 0, "Malformed")]
    [InlineData("{\"type\":\"turn.completed\"}", "{\"outcome\":\"success\"}", 9, "exited 9")]
    public async Task FakeCodexRejectsFailureIncompleteMalformedMissingStatusAndNonzero(string events, string outcome, int exit, string reason)
    {
        using var w = new TestWorkspace(); string fake = await FakeCodex(w, events, outcome, exit);
        var r = await new Engine(EngineTests.Registry(w.Root, new CodexProcessBackend(new(true, fake, w.Root)))).RunAsync(EngineTests.StageGraph(", fidelity=truncate"), w.RunDirectory(), w.Root);
        Assert.False(r.Outcome.IsSuccess); Assert.Contains(reason, r.Outcome.FailureReason);
    }
    [Fact]
    public async Task AdapterDisabledAndUnsupportedCapabilitiesNeverLaunchAProcess()
    {
        using var w = new TestWorkspace();
        foreach (var (name, enabled, fidelity, sandbox) in new[] { ("disabled", false, "truncate", "read-only"), ("full", true, "full", "read-only"), ("unsafe", true, "truncate", "danger-full-access") })
        {
            var b = new CodexProcessBackend(new(enabled, "/missing/must-not-run", w.Root, sandbox));
            var r = await new Engine(EngineTests.Registry(w.Root, b)).RunAsync(EngineTests.StageGraph($", fidelity={fidelity}"), w.RunDirectory(name), w.Root);
            Assert.False(r.Outcome.IsSuccess); Assert.Empty(Directory.GetFiles(w.RunDirectory(name), "process.pid", SearchOption.AllDirectories));
        }
    }
    internal static async Task WaitForFile(string path) => await WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
    internal static async Task WaitUntil(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew(); while (!predicate() && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10); Assert.True(predicate(), "Condition was not met within five seconds");
    }
    internal static bool IsRunning(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; }
    }
}
