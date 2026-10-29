using System.Reflection;
using System.Text.Json;
using Nightshift.Core;

namespace Nightshift.Cli;

public sealed class CliApplication(TextWriter output, TextWriter error, string invocationDirectory)
{
    public static string Version => typeof(CliApplication).Assembly.GetName().Version!.ToString(3);
    public async Task<int> RunAsync(string[] arguments, CancellationToken token = default)
    {
        Arguments? args = null;
        try
        {
            args = new(arguments);
            if (args.Command == "version") { output.WriteLine("nightshift " + Version); return 0; }
            if (args.Command == "help" || args.Has("--help")) { output.WriteLine(Help); return 0; }
            if (args.Command is not "doctor" and not "init" and not "demo" and not "validate" and not "run" and not "resume" and not "status" and not "result" and not "inspect" and not "events") throw new ArgumentException($"Unknown command '{args.Command}'. Run nightshift help.");
            bool explicitRunPath = args.Target != null && args.Command is "resume" or "status" or "result" or "inspect" or "events";
            var project = explicitRunPath ? new Project(Path.GetFullPath(invocationDirectory), null, new()) : Project.Discover(invocationDirectory, args.Get("--project"), args.Get("--config"));
            if (args.Command == "init")
            {
                project = await Project.InitializeAsync(invocationDirectory, args, token);
                if (args.Has("--json")) output.WriteLine(Json.Serialize(project));
                else
                {
                    output.WriteLine($"Initialized project: {project.Root}\nConfiguration: {project.ConfigurationPath}\nWorkflow: {project.Resolve(project.Configuration.DefaultWorkflow)}\nBackend: {project.Configuration.Backend} | Sandbox: {project.Configuration.Sandbox}");
                    if (project.Configuration.EnableCodex) output.WriteLine("Saved explicit real-worker opt-in. Future project runs may consume Codex usage.");
                    output.WriteLine("Review your workflow prompts, then run: nightshift doctor; nightshift run");
                }
                return 0;
            }
            if (args.Command == "doctor") return await Doctor(args, project, token);
            string workingDirectory = args.Get("--workdir") is { } wd ? Project.FullPath(wd, invocationDirectory) : project.Resolve(project.Configuration.WorkingDirectory);
            string outputRoot = args.Get("--output-root") is { } root ? Project.FullPath(root, invocationDirectory) : project.Resolve(project.Configuration.RunOutputRoot);
            if (args.Command is "status" or "result" or "inspect" or "events" or "resume")
            {
                if (args.Has("--latest") && args.Target != null) throw new ArgumentException("Use a run path or --latest, not both.");
                string directory = args.Has("--latest") ? RunViews.Latest(project, outputRoot, workingDirectory, args.Command == "resume") : args.Target is { } path ? Project.FullPath(path, invocationDirectory) : throw new ArgumentException($"Use nightshift {args.Command} --latest or supply a run directory.");
                if (args.Command is "inspect" or "events") { output.WriteLine(await File.ReadAllTextAsync(Path.Combine(directory, args.Command == "inspect" ? "checkpoint.json" : "events.jsonl"), token)); return 0; }
                if (args.Command is "status" or "result") { PrintView(directory, args.Has("--json"), args.Command == "result"); return 0; }
                return await Resume(args, directory, token);
            }
            if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException($"Working directory is missing: {workingDirectory}");
            if (args.Command == "demo") return await Demo(args, project, outputRoot, token);
            string workflow = args.Target is { } supplied ? Project.FullPath(supplied, invocationDirectory) : project.ConfigurationPath != null ? project.Resolve(project.Configuration.DefaultWorkflow) : throw new ArgumentException("Supply a workflow DOT file or run nightshift init to configure a default workflow.");
            if (!File.Exists(workflow)) throw new FileNotFoundException($"Workflow does not exist: {workflow}");
            if (args.Command == "validate")
            {
                var p = Preparation.Prepare(await File.ReadAllTextAsync(workflow, token)); Diagnostics(p); Preparation.RequireValid(p);
                if (args.Has("--json")) output.WriteLine(Json.Serialize(new { valid = true, nodes = p.Graph.Nodes.Count, edges = p.Graph.Edges.Count, diagnostics = p.Diagnostics }));
                else output.WriteLine($"Valid: {p.Graph.Nodes.Count} nodes, {p.Graph.Edges.Count} edges");
                return 0;
            }
            string backend = args.Get("--backend") ?? project.Configuration.Backend;
            bool enabled = args.Has("--enable-codex") || project.Configuration.EnableCodex;
            string sandbox = args.Get("--sandbox") ?? project.Configuration.Sandbox;
            var sources = new Dictionary<string, string>
            {
                ["project"] = project.ConfigurationPath ?? "invocation directory (unconfigured)",
                ["workflow"] = args.Target != null ? "command line; relative to invocation directory" : "project configuration",
                ["backend"] = args.Has("--backend") ? "command line" : project.ConfigurationPath ?? "safe default: scripted",
                ["working_directory"] = args.Has("--workdir") ? "command line" : project.ConfigurationPath ?? "invocation directory",
                ["sandbox"] = args.Has("--sandbox") ? "command line" : project.ConfigurationPath ?? "default: read-only",
                ["real_worker_opt_in"] = args.Has("--enable-codex") ? "command line" : enabled ? "explicit saved project opt-in" : "not granted",
                ["run_output_root"] = args.Has("--output-root") ? "command line; relative to invocation directory" : project.ConfigurationPath ?? "default: project .nightshift/runs",
                ["max_visits"] = args.Has("--max-visits") ? "command line" : project.ConfigurationPath ?? "default: 1000",
                ["max_seconds"] = args.Has("--max-seconds") ? "command line" : project.ConfigurationPath ?? "default: 3600",
                ["outputs"] = args.Outputs.Count > 0 ? "command line; relative to invocation directory" : project.ConfigurationPath ?? "none declared"
            };
            string? executable = null;
            if (backend == "codex")
            {
                if (!enabled) throw new ArgumentException("Real Codex workers require --enable-codex or explicit opt-in saved by nightshift init --backend codex --enable-codex.");
                var resolved = CodexDiscovery.Resolve(args.Get("--codex"), project.Configuration.CodexExecutable, invocationDirectory, project.Root); executable = resolved.Path; sources["codex_executable"] = resolved.Source;
            }
            var settings = new ExecutionSettings(project.Root, backend, executable, sandbox, enabled, outputRoot, workflow, args.Outputs.Count > 0 ? args.Outputs.Select(p => Project.FullPath(p, invocationDirectory)).ToList() : project.Configuration.Outputs.Select(project.Resolve).ToList(), sources);
            return await Execute(args, settings, workingDirectory, NewRunDirectory(args, outputRoot), Options(args, project.Configuration), false, token);
        }
        catch (OperationCanceledException) { WriteError(args, "Cancelled before a resumable run was initialized."); return 130; }
        catch (Exception e) when (e is ArgumentException or IOException or FormatException or OverflowException or PipelineException or DotParseException or JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { WriteError(args, e.Message); return 2; }
    }
    private void Diagnostics(PreparedPipeline p)
    {
        foreach (var d in p.Diagnostics) error.WriteLine($"{d.Severity} {d.Rule} {d.NodeId ?? d.Edge}: {ProgressDisplay.Clean(d.Message)}");
    }
    private void WriteError(Arguments? args, string message)
    {
        if (args?.Has("--json") == true) output.WriteLine(Json.Serialize(new { error = ProgressDisplay.Clean(message), next_action = "nightshift help" }));
        else error.WriteLine("Error: " + ProgressDisplay.Clean(message));
    }
    private static EngineOptions Options(Arguments args, ProjectConfiguration config) => new(args.Integer("--max-visits", config.MaxVisits), args.Number("--max-seconds", config.MaxSeconds), args.Has("--pause-after") ? args.Integer("--pause-after", 0) : null);
    private string NewRunDirectory(Arguments args, string root) => args.Get("--dir") is { } directory ? Project.FullPath(directory, invocationDirectory) : Path.Combine(root, DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
    private void PrintView(string dir, bool json, bool includeResponse)
    {
        var view = RunViews.Read(dir); if (json) output.WriteLine(Json.Serialize(view)); else ProgressDisplay.Print(view, output, includeResponse);
    }
    private async Task<int> Execute(Arguments args, ExecutionSettings settings, string workdir, string directory, EngineOptions options, bool resume, CancellationToken token)
    {
        if (settings.Backend is not "scripted" and not "codex") throw new ArgumentException("backend must be codex or scripted.");
        if (settings.Sandbox is not "read-only" and not "workspace-write") throw new ArgumentException("sandbox must be read-only or workspace-write; no bypass modes are supported.");
        if (!Directory.Exists(workdir)) throw new DirectoryNotFoundException($"Working directory does not exist: {workdir}");
        if (!resume && Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new ArgumentException($"Run directory is not empty: {directory}. Use resume for a saved run, or choose a fresh directory; existing files were preserved.");
        var checkpoint = resume ? Json.Read<Checkpoint>(await File.ReadAllTextAsync(Path.Combine(directory, "checkpoint.json"), token)) : null;
        if (checkpoint?.FinalOutcome != null) { PrintView(directory, args.Has("--json"), true); return checkpoint.FinalOutcome.IsSuccess ? 0 : 1; }
        settings = settings with { Limits = options with { PauseAfterStages = null } };
        ICodergenBackend backend;
        if (settings.Backend == "codex")
        {
            if (!settings.EnableCodex) throw new ArgumentException("This run has no real-worker permission. Explicit --enable-codex is required.");
            CodexDiscovery.RequireExecutable(settings.CodexExecutable ?? "");
            backend = new CodexProcessBackend(new(true, settings.CodexExecutable!, workdir, settings.Sandbox));
        }
        else backend = new ScriptedBackend();
        IInterviewer interviewer = args.Get("--answers") is { } answers ? new QueueInterviewer(answers.Split(',').Select(a => new Answer(a.Trim(), Identity: "cli-queue-unverified")))
            : Console.IsInputRedirected || args.Has("--json") ? new CallbackInterviewer((_, _) => throw new ArgumentException("Human gate needs --answers in noninteractive mode. No auto-approval occurred.")) : new ConsoleInterviewer();
        var registry = HandlerRegistry.Create(backend, interviewer, workdir);
        string dot = await File.ReadAllTextAsync(resume ? Path.Combine(directory, "graph.dot") : settings.WorkflowPath, token);
        var prepared = Preparation.Prepare(dot, registry); Diagnostics(prepared); Preparation.RequireValid(prepared);
        if (settings.Backend == "codex") foreach (var node in prepared.Graph.Nodes.Values.Where(n => n.Type == "codergen"))
        {
            var incoming = prepared.Graph.Edges.Where(e => e.To == node.Id).ToList();
            bool unsupported = incoming.Count == 0 ? Handoff.Resolve(prepared.Graph, node, null, "").Fidelity != "truncate" : incoming.Any(edge => Handoff.Resolve(prepared.Graph, node, edge, edge.From).Fidelity != "truncate");
            if (unsupported)
                throw new ArgumentException($"Codex stage '{node.Id}' requires truncate fidelity. Set graph [default_fidelity=truncate] and remove unsupported node/edge overrides.");
        }
        TextWriter progress = args.Has("--json") ? error : output;
        progress.WriteLine($"Project: {settings.ProjectRoot}\nWorkflow: {settings.WorkflowPath}\nWorkspace: {workdir}\nBackend: {backend.Capabilities.Name} | Real workers: {(settings.Backend == "codex" ? "enabled; may consume usage" : "off (simulation)")} | Sandbox: {settings.Sandbox}\nRun directory: {directory}");
        if (settings.CodexExecutable != null) progress.WriteLine("Codex executable: " + settings.CodexExecutable);
        progress.WriteLine("Settings: " + (settings.Sources.GetValueOrDefault("resume") ?? settings.Sources.GetValueOrDefault("project") ?? settings.Sources.GetValueOrDefault("mode") ?? "saved run"));
        var overrides = settings.Sources.Where(p => p.Value.StartsWith("command line", StringComparison.Ordinal) || p.Value == "explicit resume override").Select(p => p.Key).ToList();
        if (overrides.Count > 0) progress.WriteLine("CLI overrides: " + string.Join(", ", overrides));
        await using var display = new ProgressDisplay(progress);
        var engine = new Engine(registry); engine.Event += display.Handle;
        var result = await engine.RunAsync(dot, directory, workdir, options, resume, token, settings);
        if (!args.Has("--json")) output.WriteLine("Trace: " + string.Join(" -> ", result.Checkpoint.CompletedNodes));
        PrintView(directory, args.Has("--json"), true);
        return result.State == "interrupted" ? 3 : result.Outcome.IsSuccess ? 0 : 1;
    }
    private async Task<int> Resume(Arguments args, string directory, CancellationToken token)
    {
        var manifest = Json.Read<RunManifest>(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json"), token));
        var recorded = RunViews.ReadExecution(directory, manifest);
        if (recorded?.SchemaVersion is not null and not 1) throw new ArgumentException("Unsupported saved execution-settings version.");
        if (recorded == null && args.Get("--backend") == null) throw new ArgumentException("Legacy run has no recorded backend. Resume requires explicit --backend scripted or --backend codex --enable-codex. No backend was guessed.");
        string backend = recorded?.Backend ?? args.Get("--backend")!;
        if (args.Get("--backend") is { } requested && requested != backend) throw new ArgumentException("Cannot switch backend on resume. Start a fresh run to change backend.");
        string sandbox = recorded?.Sandbox ?? args.Get("--sandbox") ?? "read-only";
        if (recorded != null && args.Get("--sandbox") is { } changed && changed != sandbox) throw new ArgumentException("Cannot change sandbox on resume. Start a fresh run with the desired sandbox.");
        if (args.Get("--workdir") is { } wd && Project.FullPath(wd, invocationDirectory) != manifest.WorkingDirectory) throw new ArgumentException("Cannot change the saved workspace on resume.");
        string? exe = recorded?.CodexExecutable;
        bool enabled = recorded?.EnableCodex == true || args.Has("--enable-codex");
        var sources = new Dictionary<string, string>(recorded?.Sources ?? new Dictionary<string, string>()) { ["resume"] = "saved run identity; current project defaults ignored" };
        foreach (string field in new[] { "max-visits", "max-seconds" }) if (args.Has("--" + field)) sources[field.Replace('-', '_')] = "command line resume override";
        if (args.Get("--codex") is { } replacement) { exe = Project.FullPath(replacement, invocationDirectory); CodexDiscovery.RequireExecutable(exe); sources["codex_executable"] = "explicit resume override"; }
        else if (recorded == null && backend == "codex") exe = CodexDiscovery.Resolve(null, null, invocationDirectory, manifest.WorkingDirectory).Path;
        var settings = new ExecutionSettings(recorded?.ProjectRoot ?? manifest.WorkingDirectory, backend, exe, sandbox, enabled, recorded?.RunOutputRoot ?? Path.GetDirectoryName(directory)!, recorded?.WorkflowPath ?? Path.Combine(directory, "graph.dot"), recorded?.Outputs ?? [], sources);
        var prior = recorded?.Limits ?? manifest.Options;
        var options = new EngineOptions(args.Integer("--max-visits", prior.MaxVisits), args.Number("--max-seconds", prior.MaxSeconds), args.Has("--pause-after") ? args.Integer("--pause-after", 0) : null);
        return await Execute(args, settings, manifest.WorkingDirectory, directory, options, true, token);
    }
    private async Task<int> Doctor(Arguments args, Project project, CancellationToken token)
    {
        var checks = new List<DiagnosticCheck>(); bool required = (args.Get("--backend") ?? project.Configuration.Backend) == "codex";
        string workdir = args.Get("--workdir") is { } wd ? Project.FullPath(wd, invocationDirectory) : project.Resolve(project.Configuration.WorkingDirectory);
        string root = args.Get("--output-root") is { } o ? Project.FullPath(o, invocationDirectory) : project.Resolve(project.Configuration.RunOutputRoot);
        string sandbox = args.Get("--sandbox") ?? project.Configuration.Sandbox;
        string backend = args.Get("--backend") ?? project.Configuration.Backend;
        if (backend is not "codex" and not "scripted") checks.Add(new("Backend", "error", "Unknown backend", "Choose codex or scripted."));
        checks.Add(new("Run output root", File.Exists(root) ? "error" : "ok", root, "Choose a directory path, not an existing file."));
        if (project.ConfigurationPath != null)
        {
            string workflow = project.Resolve(project.Configuration.DefaultWorkflow);
            checks.Add(new("Default workflow", File.Exists(workflow) ? "ok" : "error", workflow, "Create the configured workflow or correct default_workflow."));
        }
        checks.Add(new("Workspace", Directory.Exists(workdir) ? "ok" : "error", workdir, "Set an existing working_directory or --workdir."));
        bool enabled = args.Has("--enable-codex") || project.Configuration.EnableCodex;
        if (required && !enabled) checks.Add(new("Real-worker permission", "error", "Not granted", "Use init --backend codex --enable-codex or explicitly enable a single run."));
        if (sandbox is not "read-only" and not "workspace-write") checks.Add(new("Sandbox", "error", "Unsupported sandbox", "Choose read-only or workspace-write."));
        ExecutableResolution? executable = null;
        try
        {
            executable = CodexDiscovery.Resolve(args.Get("--codex"), project.Configuration.CodexExecutable, invocationDirectory, project.Root);
            double seconds = args.Number("--probe-seconds", 5); if (seconds < .05 || seconds > 30) throw new ArgumentException("--probe-seconds must be 0.05..30");
            checks.AddRange(await CodexDiscovery.CheckAsync(executable, required, TimeSpan.FromSeconds(seconds), token));
        }
        catch (ArgumentException e) { checks.Add(new("Codex discovery", required || args.Has("--codex") || project.Configuration.CodexExecutable != null ? "error" : "warning", e.Message, "The offline demo does not require Codex.")); }
        var report = new DoctorReport(Version, ".NET " + Environment.Version, AppContext.BaseDirectory, project.Root, project.ConfigurationPath, workdir, root, args.Get("--backend") ?? project.Configuration.Backend, sandbox, enabled, executable, checks);
        if (args.Has("--json")) output.WriteLine(Json.Serialize(report));
        else
        {
            output.WriteLine($"Nightshift {report.Version} | {report.Runtime}\nInstallation: {report.Installation}\nProject: {report.Project}\nConfig: {report.Configuration ?? "none; run nightshift init for saved defaults"}\nWorkspace: {workdir}\nRun output root: {root}\nBackend: {report.Backend} | Sandbox: {sandbox} | Real-worker opt-in: {enabled}");
            if (executable != null) output.WriteLine($"Codex: {executable.Path} ({executable.Source})");
            foreach (var c in checks) { output.WriteLine($"[{c.State}] {c.Name}: {c.Detail}"); if (c.State != "ok") output.WriteLine("  Next: " + c.NextAction); }
            output.WriteLine("No model was started. Stored authentication does not verify live model access.");
        }
        return report.Healthy ? 0 : 1;
    }
    private async Task<int> Demo(Arguments args, Project project, string root, CancellationToken token)
    {
        string backend = args.Get("--backend") ?? "scripted";
        bool real = backend == "codex";
        if (real && !args.Has("--enable-codex")) throw new ArgumentException("A real demo requires demo --backend codex --enable-codex. The default demo is offline.");
        if (args.Has("--write") && !real) throw new ArgumentException("--write selects the real Codex writing demo; use --backend codex --enable-codex --sandbox workspace-write.");
        string sandbox = args.Get("--sandbox") ?? "read-only";
        if (args.Has("--write") && sandbox != "workspace-write") throw new ArgumentException("The writing demo requires explicit --sandbox workspace-write.");
        string scratch = Path.GetFullPath(Path.Combine(root, ".workspaces", Guid.NewGuid().ToString("N")));
        string? executable = real ? CodexDiscovery.Resolve(args.Get("--codex"), null, invocationDirectory, project.Root).Path : null;
        Directory.CreateDirectory(scratch);
        // Create a private Git repository for the worker without making a commit.
        var git = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = scratch, RedirectStandardOutput = true, RedirectStandardError = true };
        git.ArgumentList.Add("init"); git.ArgumentList.Add("-q");
        using (var process = System.Diagnostics.Process.Start(git)!) { await process.WaitForExitAsync(token); if (process.ExitCode != 0) throw new IOException("Could not initialize the scratch Git repository."); }
        string asset = Project.Asset(real ? args.Has("--write") ? "codex-write.dot" : "codex-read.dot" : "demo.dot");
        string graph = Path.Combine(scratch, "workflow.dot");
        string directory = NewRunDirectory(args, root);
        string seed = "NIGHTSHIFT_SMOKE_" + Guid.NewGuid().ToString("N");
        if (real && !args.Has("--write")) await File.WriteAllTextAsync(Path.Combine(scratch, "smoke-input.txt"), seed + "\n", token);
        string source = await File.ReadAllTextAsync(asset, token);
        if (real && !args.Has("--write"))
        {
            static string ShellQuote(string text) => "'" + text.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
            string response = Path.Combine(directory, "inspect", "visit-0001", "attempt-0001", "last-message.json");
            string command = $"grep -Fq -- {ShellQuote(seed)} {ShellQuote(response)} && printf '%s\\n' {ShellQuote(seed)} | cmp -s - smoke-input.txt";
            source = source.Replace("DEMO_READ_VERIFY", command.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal), StringComparison.Ordinal);
        }
        await File.WriteAllTextAsync(graph, source, token);
        var settings = new ExecutionSettings(project.Root, backend, executable, sandbox, real, root, graph, [Path.Combine(scratch, !real ? "demo-output.txt" : args.Has("--write") ? "codex-output.txt" : "smoke-input.txt")], new Dictionary<string, string> { ["mode"] = real ? "explicit real demo" : "offline demo; scripted worker and local shell verification" });
        var options = new EngineOptions(args.Integer("--max-visits", real ? 4 : 5), args.Number("--max-seconds", real ? 180 : 30), args.Has("--pause-after") ? args.Integer("--pause-after", 0) : null);
        if (!args.Has("--json")) output.WriteLine(real ? "Real Codex demo: one model stage, zero automatic retries; runtime limit " + options.MaxSeconds + "s." : "Offline demo: simulated model planning, local file creation and independent byte comparison; no account or model.");
        int code = await Execute(args, settings, scratch, directory, options, false, token);
        return code;
    }
    public const string Help = """
Nightshift — personal DOT workflows
  nightshift --version
  nightshift doctor [--json] [--codex PATH]
  nightshift demo                         Offline, no account or model
  nightshift demo --backend codex --enable-codex [--write --sandbox workspace-write]
  nightshift init [--backend scripted|codex] [--enable-codex]
  nightshift validate [WORKFLOW.dot]
  nightshift run [WORKFLOW.dot] [--dir RUN] [--pause-after N]
  nightshift resume RUN | --latest
  nightshift status RUN | --latest
  nightshift result RUN | --latest
  nightshift inspect RUN                  Raw checkpoint JSON
  nightshift events RUN                   Raw JSONL events

Project options: --project DIR, --config FILE, --workdir DIR, --workflow FILE (init)
Worker options: --backend scripted|codex, --enable-codex, --codex PATH,
                --sandbox read-only|workspace-write
Run options: --output-root DIR, --output PATH (repeatable declared outputs),
             --answers A,R,A, --max-visits N, --max-seconds N, --json
Configured project defaults avoid repeated flags. Explicit CLI paths are relative to
where you invoke the command; config paths are relative to nightshift.json.
Resume restores saved backend/consent/workspace/sandbox; it never falls back to simulation.
Codex supports truncate fidelity only. Every stage starts fresh; effects may replay.
Exit codes: 0 success, 1 failed/incomplete, 2 input error, 3 pause/interrupted, 130 early cancellation.
""";
}
