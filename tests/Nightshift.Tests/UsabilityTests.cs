using System.Text.Json;
using Nightshift.Cli;
using Nightshift.Core;

namespace Nightshift.Tests;

public class UsabilityTests
{
    internal static async Task<(int Code, string Output, string Error)> Invoke(string directory, params string[] args)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        int code = await new CliApplication(output, error, directory).RunAsync(args);
        return (code, output.ToString(), error.ToString());
    }
    internal static async Task<string> Fake(TestWorkspace w, string body = "")
    {
        string path = Path.Combine(w.Root, "fake worker with spaces");
        string script = """
        #!/bin/sh
        case "$1:$2" in
          --version:) echo 'codex-cli 1.2.3'; echo 'DO_NOT_LEAK_SECRET'; exit 0 ;;
          exec:--help) echo '--json --ephemeral --sandbox --output-schema --output-last-message'; exit 0 ;;
          login:status) echo 'DO_NOT_LEAK_AUTH'; exit 0 ;;
        esac
        printf '%s\n' "$@" > "$ATTRACTOR_STAGE_DIR/worker-arguments.txt"
        output=''
        while [ "$#" -gt 0 ]; do
          if [ "$1" = '--output-last-message' ]; then shift; output=$1; fi
          shift
        done
        cat > "$ATTRACTOR_STAGE_DIR/worker-input.txt"
        """ + "\n" + (body.Length == 0 ? "printf '%s' '{\"outcome\":\"success\",\"notes\":\"fake worker claim\"}' > \"$output\"\n" : body + "\n") + "printf '%s\\n' '{\"type\":\"turn.completed\"}'\n";
        await File.WriteAllTextAsync(path, script);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    [Fact]
    public async Task InitPreservesExistingWorkflowIgnoreRulesAndExistingConfiguration()
    {
        using var w = new TestWorkspace(); string workflow = Path.Combine(w.Root, "workflow.dot"); string ignore = Path.Combine(w.Root, ".gitignore");
        await File.WriteAllTextAsync(workflow, "existing workflow"); await File.WriteAllTextAsync(ignore, "existing-rule");
        var result = await Invoke(w.Root, "init"); Assert.Equal(0, result.Code);
        Assert.Equal("existing workflow", await File.ReadAllTextAsync(workflow));
        Assert.Equal("existing-rule\n/.nightshift/runs/\n", await File.ReadAllTextAsync(ignore));
        string config = await File.ReadAllTextAsync(Path.Combine(w.Root, "nightshift.json"));
        Assert.Equal(2, (await Invoke(w.Root, "init", "--backend", "codex", "--enable-codex")).Code);
        Assert.Equal(config, await File.ReadAllTextAsync(Path.Combine(w.Root, "nightshift.json")));
    }
    [Fact]
    public async Task NestedDiscoveryConfigurationPathsAndCliOverridesUseTheirOwnOrigins()
    {
        using var w = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(w.Root, ".git")); Directory.CreateDirectory(Path.Combine(w.Root, "nested")); Directory.CreateDirectory(Path.Combine(w.Root, "workspace"));
        await AtomicFile.WriteJsonAsync(Path.Combine(w.Root, "nightshift.json"), new ProjectConfiguration { WorkingDirectory = "workspace", Outputs = ["report.txt"], MaxVisits = 3 });
        await File.WriteAllTextAsync(Path.Combine(w.Root, "workflow.dot"), EngineTests.StageGraph());
        string nested = Path.Combine(w.Root, "nested");
        var result = await Invoke(nested, "run", "--dir", "cli-run", "--workdir", ".", "--json"); Assert.Equal(0, result.Code);
        var manifest = Json.Read<RunManifest>(await File.ReadAllTextAsync(Path.Combine(nested, "cli-run", "manifest.json")));
        Assert.Equal(nested, manifest.WorkingDirectory); Assert.Equal(w.Root, manifest.Execution!.ProjectRoot); Assert.Equal(Path.Combine(w.Root, "report.txt"), manifest.Execution.Outputs[0]);
        Assert.Equal(Path.Combine(w.Root, "workflow.dot"), manifest.Execution.WorkflowPath);
        using var json = JsonDocument.Parse(result.Output); Assert.Equal("success", json.RootElement.GetProperty("state").GetString());
        var discovered = Project.Discover(nested); Assert.Equal(w.Root, discovered.Root);
        Directory.CreateDirectory(Path.Combine(nested, ".git")); Assert.Null(Project.Discover(nested).ConfigurationPath);
        var direct = Project.Discover(nested, explicitConfig: "../nightshift.json"); Assert.Equal(w.Root, direct.Root);
    }
    [Theory]
    [InlineData("{\"schema_version\":2}")]
    [InlineData("{\"backend\":\"unrecognized\"}")]
    [InlineData("{\"max_visits\":0}")]
    [InlineData("{\"max_seconds\":-1}")]
    [InlineData("{\"sandbox\":\"danger-full-access\"}")]
    [InlineData("{\"codex_executable\":\"\"}")]
    [InlineData("{\"unknown_option\":true}")]
    [InlineData("not json")]
    public async Task MalformedConfigurationIsActionableAndDoesNotInitializeARun(string config)
    {
        using var w = new TestWorkspace(); await File.WriteAllTextAsync(Path.Combine(w.Root, "nightshift.json"), config);
        var r = await Invoke(w.Root, "run", "--json"); Assert.Equal(2, r.Code); using var json = JsonDocument.Parse(r.Output); Assert.True(json.RootElement.TryGetProperty("error", out _)); Assert.False(Directory.Exists(Path.Combine(w.Root, ".nightshift")));
    }
    [Fact]
    public async Task CodexDiscoveryUsesExplicitThenConfigThenPathThenBundleAndDoesNotFallThroughInvalidExplicitValues()
    {
        using var w = new TestWorkspace(); string exe = await Fake(w); string directory = Path.Combine(w.Root, "bin"); Directory.CreateDirectory(directory);
        string onPath = Path.Combine(directory, "codex"); File.Copy(exe, onPath);
        Assert.Equal("command line", CodexDiscovery.Resolve(exe, exe, w.Root, w.Root, directory, exe).Source);
        Assert.Equal("project configuration", CodexDiscovery.Resolve(null, exe, w.Root, w.Root, directory, exe).Source);
        Assert.Equal(onPath, CodexDiscovery.Resolve(null, null, w.Root, w.Root, directory, exe).Path);
        Assert.Equal("macOS application bundle", CodexDiscovery.Resolve(null, null, w.Root, w.Root, "", exe).Source);
        foreach (string invalid in new[] { "", directory, "not-present", "codex='some alias'" }) Assert.Throws<ArgumentException>(() => CodexDiscovery.Resolve(invalid, exe, w.Root, w.Root, directory, exe));
        string nonExecutable = Path.Combine(w.Root, "nonexecutable"); await File.WriteAllTextAsync(nonExecutable, "not executable");
        Assert.Throws<ArgumentException>(() => CodexDiscovery.Resolve(nonExecutable, null, w.Root, w.Root, directory, exe));
        Assert.Throws<ArgumentException>(() => CodexDiscovery.Resolve(null, "missing", w.Root, w.Root, directory, exe));
        Assert.Throws<ArgumentException>(() => CodexDiscovery.Resolve(null, null, w.Root, w.Root, "", ""));
    }
    [Fact]
    public async Task DoctorOnlyUsesBoundedReadOnlyProbesAndDoesNotPrintAuthenticationOutput()
    {
        using var w = new TestWorkspace(); string fake = await Fake(w);
        var result = await Invoke(w.Root, "doctor", "--backend", "codex", "--enable-codex", "--codex", fake, "--json");
        Assert.Equal(0, result.Code); using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("codex", doc.RootElement.GetProperty("backend").GetString()); Assert.Contains("live model access is unverified", result.Output);
        Assert.DoesNotContain("DO_NOT_LEAK", result.Output + result.Error); Assert.Empty(Directory.GetFiles(w.Root, "worker-input.txt", SearchOption.AllDirectories));
        var noPermission = await Invoke(w.Root, "doctor", "--backend", "codex", "--codex", fake); Assert.Equal(1, noPermission.Code);
        string stalled = Path.Combine(w.Root, "stalled"); await File.WriteAllTextAsync(stalled, "#!/bin/sh\nsleep 30\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stalled, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var timeout = await Invoke(w.Root, "doctor", "--backend", "codex", "--enable-codex", "--codex", stalled, "--probe-seconds", ".1");
        Assert.Equal(1, timeout.Code); Assert.Contains("timed out", timeout.Output); Assert.DoesNotContain("DO_NOT_LEAK", timeout.Output + timeout.Error);
    }
    [Fact]
    public async Task SavedOptInRunsFakeCodexAndResumeIgnoresChangedProjectDefaults()
    {
        using var w = new TestWorkspace(); string fake = await Fake(w);
        Assert.Equal(2, (await Invoke(w.Root, "init", "--backend", "codex")).Code);
        Assert.False(File.Exists(Path.Combine(w.Root, "nightshift.json")));
        Assert.Equal(0, (await Invoke(w.Root, "init", "--backend", "codex", "--enable-codex", "--codex", fake)).Code);
        var pause = await Invoke(w.Root, "run", "--pause-after", "1"); Assert.Equal(3, pause.Code);
        string run = Directory.GetDirectories(Path.Combine(w.Root, ".nightshift", "runs")).Single();
        var m = Json.Read<RunManifest>(await File.ReadAllTextAsync(Path.Combine(run, "manifest.json"))); Assert.Equal("codex", m.Execution!.Backend); Assert.True(m.Execution.EnableCodex); Assert.Equal(fake, m.Execution.CodexExecutable);
        await AtomicFile.WriteJsonAsync(Path.Combine(w.Root, "nightshift.json"), new ProjectConfiguration { Backend = "scripted", Sandbox = "workspace-write", WorkingDirectory = "missing", MaxVisits = 1 });
        var resumed = await Invoke(w.Root, "resume", "--latest"); Assert.Equal(0, resumed.Code); Assert.Contains("codex-process", resumed.Output); Assert.Contains("fake worker claim", resumed.Output);
        var view = RunViews.Read(run); Assert.Equal("codex", view.Backend); Assert.Equal("read-only", view.Sandbox); Assert.NotNull(view.Response);
        Assert.Equal(2, (await Invoke(w.Root, "resume", run, "--backend", "scripted")).Code);
        Assert.Equal(2, (await Invoke(w.Root, "resume", run, "--sandbox", "workspace-write")).Code);
        await File.WriteAllTextAsync(Path.Combine(w.Root, "nightshift.json"), "invalid project defaults");
        Assert.Equal(0, (await Invoke(w.Root, "resume", run)).Code); // Explicit saved run does not depend on current config validity.
    }
    [Fact]
    public async Task ExecutableReplacementIsRecordedAndUsedBySubsequentResume()
    {
        using var w = new TestWorkspace(); string fake = await Fake(w); string replacement = Path.Combine(w.Root, "replacement"); File.Copy(fake, replacement);
        await Invoke(w.Root, "init", "--backend", "codex", "--enable-codex", "--codex", fake);
        await Invoke(w.Root, "run", "--pause-after", "1"); string run = Directory.GetDirectories(Path.Combine(w.Root, ".nightshift", "runs")).Single();
        var resumed = await Invoke(w.Root, "resume", run, "--codex", replacement, "--pause-after", "1"); Assert.Equal(3, resumed.Code);
        var execution = Json.Read<ExecutionSettings>(await File.ReadAllTextAsync(Path.Combine(run, "execution.json"))); Assert.Equal(replacement, execution.CodexExecutable);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(run, "execution-history")));
        var final = await Invoke(w.Root, "resume", run); Assert.Equal(0, final.Code); Assert.Equal(replacement, RunViews.ReadExecution(run, Json.Read<RunManifest>(await File.ReadAllTextAsync(Path.Combine(run, "manifest.json"))))!.CodexExecutable);
    }
    [Fact]
    public async Task LegacyResumeRequiresExplicitBackendAndPermission()
    {
        using var w = new TestWorkspace(); string graph = EngineTests.StageGraph();
        await new Engine(EngineTests.Registry(w.Root)).RunAsync(graph, w.RunDirectory(), w.Root, new(PauseAfterStages: 1));
        Assert.Equal(2, (await Invoke(w.Root, "resume", w.RunDirectory())).Code);
        Assert.Equal(2, (await Invoke(w.Root, "resume", w.RunDirectory(), "--backend", "codex", "--codex", await Fake(w))).Code);
        Assert.Equal(0, (await Invoke(w.Root, "resume", w.RunDirectory(), "--backend", "scripted")).Code);
    }
    [Fact]
    public async Task LatestSelectsOnlyThisProjectAndOnlyResumableRunsForResume()
    {
        using var w = new TestWorkspace(); await Invoke(w.Root, "init");
        await Invoke(w.Root, "run", "--pause-after", "1");
        string first = Directory.GetDirectories(Path.Combine(w.Root, ".nightshift", "runs")).Single();
        await Invoke(w.Root, "run");
        var p = Project.Discover(w.Root); string root = p.Resolve(p.Configuration.RunOutputRoot);
        Assert.NotEqual(first, RunViews.Latest(p, root, w.Root, false)); Assert.Equal(first, RunViews.Latest(p, root, w.Root, true));
        string unrelated = Path.Combine(root, "unrelated"); Directory.CreateDirectory(unrelated);
        var m = Json.Read<RunManifest>(await File.ReadAllTextAsync(Path.Combine(first, "manifest.json")));
        await AtomicFile.WriteJsonAsync(Path.Combine(unrelated, "manifest.json"), m with { StartedAt = DateTimeOffset.UtcNow.AddHours(1), Execution = m.Execution! with { ProjectRoot = Path.Combine(w.Root, "other") } });
        File.Copy(Path.Combine(first, "checkpoint.json"), Path.Combine(unrelated, "checkpoint.json"));
        Assert.Equal(first, RunViews.Latest(p, root, w.Root, true));
        Assert.Equal(0, (await Invoke(w.Root, "resume", "--latest")).Code);
        Assert.Equal(2, (await Invoke(w.Root, "resume", "--latest")).Code);
        Assert.Equal(0, (await Invoke(w.Root, "result", "--latest", "--json")).Code);
    }
    [Fact]
    public async Task OfflineDemoIsSelfContainedAndExposesProgressOutputAndVerifierVerdicts()
    {
        using var w = new TestWorkspace(); var demo = await Invoke(w.Root, "demo"); Assert.Equal(0, demo.Code);
        Assert.Contains("Offline demo", demo.Output); Assert.Contains("Stage create: running", demo.Output); Assert.Contains("Tool verdict: verify: success", demo.Output); Assert.Contains("Declared output (exists", demo.Output);
        Assert.DoesNotContain('\u001b', demo.Output); Assert.DoesNotContain("--enable-codex", demo.Output);
        var p = Project.Discover(w.Root); string run = RunViews.Latest(p, p.Resolve(p.Configuration.RunOutputRoot), w.Root, false);
        var view = RunViews.Read(run); Assert.Equal("NIGHTSHIFT_DEMO_OK\n", await File.ReadAllTextAsync(view.Outputs.Single().Path));
        Assert.Equal(0, (await Invoke(w.Root, "status", "--latest")).Code); Assert.Equal(0, (await Invoke(w.Root, "result", "--latest")).Code);
        Assert.Equal(2, (await Invoke(w.Root, "demo", "--backend", "codex")).Code);
        Assert.Equal(2, (await Invoke(w.Root, "demo", "--write")).Code);
    }
    [Fact]
    public async Task RealDemoUsesFakeProcessesAndIndependentReadOrWriteVerification()
    {
        using var w = new TestWorkspace();
        string fake = await Fake(w, "if [ -f smoke-input.txt ]; then value=$(cat smoke-input.txt); else value=written; printf 'NIGHTSHIFT_WRITE_OK\\n' > codex-output.txt; fi\nprintf '{\"outcome\":\"success\",\"notes\":\"%s\"}' \"$value\" > \"$output\"");
        var read = await Invoke(w.Root, "demo", "--backend", "codex", "--enable-codex", "--codex", fake, "--dir", "read"); Assert.True(read.Code == 0, read.Output + read.Error); Assert.Contains("Tool verdict: verify: success", read.Output);
        var write = await Invoke(w.Root, "demo", "--backend", "codex", "--enable-codex", "--codex", fake, "--write", "--sandbox", "workspace-write", "--dir", "write"); Assert.Equal(0, write.Code);
        string liar = await Fake(w); // Same fake executable now makes a success claim without reading the unique seed.
        var missing = await Invoke(w.Root, "demo", "--backend", "codex", "--enable-codex", "--codex", liar, "--dir", "liar"); Assert.Equal(1, missing.Code); Assert.Contains("verify: fail", missing.Output); Assert.Equal("failed", RunViews.Read(Path.Combine(w.Root, "liar")).State);
    }
    [Fact]
    public async Task QuietWorkerHeartbeatAndJsonOutputStayUsable()
    {
        using var writer = new StringWriter();
        await using (var progress = new ProgressDisplay(writer, TimeSpan.FromMilliseconds(10)))
        {
            progress.Handle(new(RunEventKind.StageStarted, "waiting", 2, 3)); await Task.Delay(40);
            progress.Handle(new(RunEventKind.StageFailed, "waiting", 2, 3, "retry")); progress.Handle(new(RunEventKind.StageRetrying, "waiting", 2, 3, "delay_ms=100"));
        }
        Assert.Contains("still running", writer.ToString()); Assert.Contains("visit 2, attempt 3", writer.ToString()); Assert.DoesNotContain('%', writer.ToString());
        using var w = new TestWorkspace(); var demo = await Invoke(w.Root, "demo", "--json"); Assert.Equal(0, demo.Code); using var json = JsonDocument.Parse(demo.Output); Assert.Equal("success", json.RootElement.GetProperty("state").GetString()); Assert.Contains("Stage", demo.Error);
    }
    [Fact]
    public async Task UnsupportedFidelityIsRejectedBeforeFakeWorkerLaunchAndIncomingEdgeOverridesAreHonored()
    {
        using var w = new TestWorkspace(); string fake = await Fake(w);
        await Invoke(w.Root, "init", "--backend", "codex", "--enable-codex", "--codex", fake);
        string workflow = Path.Combine(w.Root, "workflow.dot"); await File.WriteAllTextAsync(workflow, EngineTests.StageGraph());
        var bad = await Invoke(w.Root, "run"); Assert.Equal(2, bad.Code); Assert.Contains("truncate fidelity", bad.Error);
        await File.WriteAllTextAsync(workflow, EngineTests.StageGraph("", "start -> work [fidelity=truncate, weight=2]"));
        // All possible incoming edges must resolve to a supported mode.
        Assert.Equal(2, (await Invoke(w.Root, "run")).Code);
        await File.WriteAllTextAsync(workflow, "digraph G { start [shape=Mdiamond] work [prompt=x] exit [shape=Msquare] start -> work [fidelity=truncate] work -> exit }");
        Assert.Equal(0, (await Invoke(w.Root, "run")).Code);
    }
}
