using System.Text.Json;
using Nightshift.Adapters;
using Nightshift.Core;

namespace Nightshift.Cli;

public sealed class ProtectedCli(TextWriter output, TextWriter error, string invocation)
{
    public async Task<int> RunAsync(Arguments args, CancellationToken token)
    {
        string project = args.Get("--project") is { } p ? Project.FullPath(p, invocation) : Path.GetFullPath(invocation);
        string? stateRoot = args.Get("--state-root") is { } s ? Project.FullPath(s, invocation) : null;
        if (args.Target != null && args.Command is "status" or "result" or "resume" or "inspect" or "events")
        {
            string explicitRun = Project.FullPath(args.Target, invocation);
            if (!ProtectedState.Within(explicitRun, Path.Combine(stateRoot ?? ProtectedState.DefaultRoot, "projects")))
                throw new IOException("Protected run must be under the selected external state root; legacy resume is refused");
            string identityFile = Path.Combine(explicitRun, "protected-identity.json");
            if (!File.Exists(identityFile)) throw new IOException("Protected run identity is missing; legacy reconstruction is refused");
            project = Json.Read<ProtectedIdentity>(await File.ReadAllTextAsync(identityFile, token)).ProjectRoot;
        }
        if (args.Command == "demo") return await Demo(args, stateRoot, token);
        if (args.Command is "init" or "adopt")
        {
            var state = new ProtectedState(project, stateRoot);
            string path = Project.FullPath(args.Get("--policy") ?? "nightshift-policy.json", invocation);
            if (args.Command == "init")
            {
                if (File.Exists(path)) throw new ArgumentException("Policy already exists; edit and explicitly adopt it");
                string workflow = args.Get("--workflow") is { } wf ? await File.ReadAllTextAsync(Project.FullPath(wf, invocation), token)
                    : "digraph task { graph [default_fidelity=truncate]; start -> work -> exit; work [prompt=\"Describe the requested task\"]; }";
                var policy = new ApprovedPolicy(workflow, args.Get("--backend") ?? "scripted", args.Get("--agent-executable") ?? "/usr/local/bin/codex", ContainerProfile.LocalDefault,
                    [new("candidate-check", "test -f candidate.txt")], [], args.Get("--repository-service") ?? "none", args.Get("--pipeline-service") ?? "none", args.Has("--enable-agent"));
                await AtomicFile.WriteJsonAsync(path, policy, token);
                output.WriteLine("Created reviewable policy: " + path + "\nEdit task and checks, then: nightshift adopt --policy " + path); return 0;
            }
            var approved = Json.Read<ApprovedPolicy>(await File.ReadAllTextAsync(path, token));
            var registry = HandlerRegistry.Create(new ScriptedBackend(), new QueueInterviewer([]), project);
            Preparation.RequireValid(Preparation.Prepare(approved.Workflow, registry));
            string revision = await state.AdoptAsync(approved, token);
            output.WriteLine("Adopted immutable policy " + revision + "\nProtected state: " + state.ProjectDirectory); return 0;
        }
        if (args.Command == "doctor")
        {
            var state = new ProtectedState(project, stateRoot); var checks = new List<object>(); int exit = 0;
            ContainerProfile runtime = ContainerProfile.LocalDefault;
            try { var approved = state.Policy(); runtime = approved.Policy.Runtime; checks.Add(new { capability = "configured", state = "yes", policy = approved.Revision, agent = approved.Policy.Backend, repository = approved.Policy.RepositoryService, pipeline = approved.Policy.PipelineService }); }
            catch (ArgumentException e) { checks.Add(new { capability = "configured", state = "no", detail = e.Message }); }
            try { var actual = await new PodmanLauncher(runtime, new WorkspaceSnapshot([])).VerifyAsync(token); checks.Add(new { capability = "runtime-prerequisites", state = "available", identity = actual }); }
            catch (Exception e) when (e is IOException or OperationCanceledException or System.ComponentModel.Win32Exception) { checks.Add(new { capability = "runtime-prerequisites", state = "unavailable", detail = e.Message }); exit = 1; }
            checks.Add(new { capability = "fixture-contract-tests", state = "not probed by doctor; see shipped phase-three evidence" });
            checks.Add(new { capability = "actual-isolation-verification", state = "not inferred from runtime availability; run nightshift reference for an exercised workflow; hostile suite evidence is platform-specific" });
            checks.Add(new { capability = "authenticated-live-integration", state = "unexecuted" });
            output.WriteLine(Json.Serialize(new { profile = "linux-container-v1", state_root = state.Root, checks })); return exit;
        }
        if (args.Command == "reference") return await Reference(args, project, stateRoot, token);
        var store = new ProtectedState(project, stateRoot);
        string? directory = args.Target is { } target && args.Command is "status" or "result" or "resume" or "inspect" or "events" ? Project.FullPath(target, invocation) : null;
        if (directory == null && args.Has("--latest")) directory = Latest(store, args.Command == "resume");
        if (args.Command is "status" or "result" or "inspect" or "events")
        {
            if (directory == null) throw new ArgumentException("Supply a protected run path or --latest");
            store.RequireRun(directory);
            if (args.Command is "inspect" or "events") output.WriteLine(await File.ReadAllTextAsync(Path.Combine(directory, args.Command == "inspect" ? "checkpoint.json" : "events.jsonl"), token));
            else await Print(directory, args, token);
            return 0;
        }
        bool resume = args.Command == "resume";
        if (resume && directory == null) throw new ArgumentException("Supply a protected run path or --latest");
        if (!resume && (args.Target != null || args.Has("--workflow") || args.Has("--backend") || args.Has("--agent-executable") || args.Has("--enable-agent")
            || args.Has("--repository-service") || args.Has("--pipeline-service") || args.Has("--workdir") || args.Has("--output-root") || args.Has("--codex") || args.Has("--sandbox")))
            throw new ArgumentException("Protected runs use adopted policy snapshots. Edit and adopt a policy explicitly to change workflow/agent/services/runtime; run accepts only bounds and run identity");
        if (resume && new[] { "--backend", "--agent-executable", "--repository-service", "--pipeline-service", "--sandbox", "--codex", "--workdir", "--workflow" }.Any(args.Has))
            throw new ArgumentException("Protected resume cannot change recorded execution identity");
        directory ??= args.Get("--dir") is { } dir ? Project.FullPath(dir, invocation) : store.NewRun();
        EngineOptions? options = null;
        var policyForLimits = store.Policy(resume ? Json.Read<ProtectedIdentity>(File.ReadAllText(Path.Combine(directory, "protected-identity.json"))).PolicyRevision : null).Policy;
        var savedLimits = resume ? RunViews.ReadExecution(directory, Json.Read<RunManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json"))))?.Limits : null;
        if (args.Has("--pause-after") || args.Has("--max-visits") || args.Has("--max-seconds"))
            options = new(args.Integer("--max-visits", savedLimits?.MaxVisits ?? policyForLimits.MaxVisits), args.Number("--max-seconds", savedLimits?.MaxSeconds ?? policyForLimits.MaxSeconds), args.Has("--pause-after") ? args.Integer("--pause-after", 0) : null);
        return await Execute(store, directory, resume, options, args, token);
    }
    private async Task<int> Execute(ProtectedState state, string directory, bool resume, EngineOptions? options, Arguments args, CancellationToken token)
    {
        var workflow = new ProtectedWorkflow(state);
        await using var progress = new ProgressDisplay(args.Has("--json") ? error : output); workflow.Event += progress.Handle;
        IInterviewer interviewer = args.Get("--answers") is { } answers ? new QueueInterviewer(answers.Split(',').Select(a => new Answer(a, Identity: "operator-queue-unverified")))
            : new CallbackInterviewer((_, _) => throw new ArgumentException("Protected human gate requires explicit --answers in this CLI profile"));
        var result = await workflow.RunAsync(directory, resume, options, interviewer, token);
        await Print(directory, args, token);
        return result.Pipeline.State == "interrupted" ? 3 : result.Pipeline.State == "success" && result.Acceptance.Accepted ? 0 : 1;
    }
    private async Task Print(string directory, Arguments args, CancellationToken token)
    {
        var view = RunViews.Read(directory); var identity = Json.Read<ProtectedIdentity>(await File.ReadAllTextAsync(Path.Combine(directory, "protected-identity.json"), token));
        var snapshot = ProtectedState.ReadSnapshot(directory);
        var policy = Json.Read<ApprovedPolicy>(await File.ReadAllTextAsync(Path.Combine(directory, "approved-policy.json"), token));
        var candidate = new Candidate(new("local", Engine.Hash(identity.ProjectRoot)[..24], "workspace"), "snapshot:" + snapshot.Digest, snapshot.Digest);
        var records = EvidenceLedger.Read(directory);
        var decision = view.State == "success" ? Acceptance.Decide(candidate, identity.PolicyRevision, policy.Checks.Select(c => c.Id), records) : new(false, "Pipeline incomplete/interrupted/failed");
        if (args.Has("--json")) output.WriteLine(Json.Serialize(new { run = view, provenance = "protected coordinator record", identity, candidate, acceptance = decision, evidence = records }));
        else { ProgressDisplay.Print(view, output, args.Command is "result" or "run" or "resume" or "demo"); output.WriteLine("Profile: " + identity.Runtime.Profile + " | Policy: " + identity.PolicyRevision + "\nCandidate: " + candidate.SnapshotDigest + "\nCode acceptance: " + decision.Accepted + " — " + decision.Reason); }
    }
    private static string Latest(ProtectedState state, bool resume)
    {
        string root = Path.Combine(state.ProjectDirectory, "runs");
        if (!Directory.Exists(root)) throw new ArgumentException("No protected runs for this project; legacy runs remain inspectable with --profile native-development");
        return Directory.GetDirectories(root).OrderDescending(StringComparer.Ordinal).FirstOrDefault(p => File.Exists(Path.Combine(p, "checkpoint.json"))
            && (!resume || Resumable(p))) ?? throw new ArgumentException("No matching protected run");
    }
    private static bool Resumable(string directory)
    {
        var checkpoint = Json.Read<Checkpoint>(File.ReadAllText(Path.Combine(directory, "checkpoint.json")));
        if (checkpoint.FinalOutcome == null) return true;
        if (checkpoint.State != "success") return false;
        string path = Path.Combine(directory, "acceptance.json");
        if (!File.Exists(path)) return true;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var decision = doc.RootElement.TryGetProperty("decision", out var nested) ? nested : doc.RootElement;
        return !decision.TryGetProperty("accepted", out var accepted) || accepted.ValueKind != JsonValueKind.True;
    }
    private async Task<int> Demo(Arguments args, string? stateRoot, CancellationToken token)
    {
        string root = stateRoot ?? ProtectedState.DefaultRoot;
        string project = Path.Combine(Path.GetDirectoryName(root)!, "demo-projects", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "input.txt"), "offline demo\n", token);
        var state = new ProtectedState(project, stateRoot);
        var policy = new ApprovedPolicy("digraph demo { start -> plan -> write -> exit; plan [prompt=\"Plan demo\"]; write [type=tool, tool_command=\"printf 'demo verified\\n' > candidate.txt\"]; }",
            "scripted", "/bin/sh", ContainerProfile.LocalDefault, [new("exact-bytes", "printf 'demo verified\\n' | cmp -s - candidate.txt")], []);
        await state.AdoptAsync(policy, token);
        output.WriteLine(args.Has("--json") ? "" : "Offline protected demo: no model or account. Project: " + project);
        return await Execute(state, state.NewRun(), false, null, args, token);
    }
    private async Task<int> Reference(Arguments args, string project, string? stateRoot, CancellationToken token)
    {
        var reference = await ReferenceWorkflow.RunAsync(new ProtectedState(project, stateRoot), args.Get("--repository-service") ?? "github", args.Get("--pipeline-service") ?? "github-actions", token);
        output.WriteLine(Json.Serialize(reference)); return reference.Accepted && reference.StaleRejected && reference.Dispatches == 1 ? 0 : 1;
    }
}
