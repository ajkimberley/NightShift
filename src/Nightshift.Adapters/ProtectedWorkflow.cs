using System.Text;
using Nightshift.Core;

namespace Nightshift.Adapters;

public sealed record ProtectedResult(RunResult Pipeline, Candidate Candidate, AcceptanceDecision Acceptance, string RunDirectory);
public sealed class ProtectedWorkflow(ProtectedState state)
{
    public event Action<RunEvent>? Event;
    public async Task<ProtectedResult> RunAsync(string directory, bool resume, EngineOptions? options, IInterviewer interviewer, CancellationToken token)
    {
        state.RequireRun(directory);
        if (!resume && Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("New protected run directory is not empty");
        Directory.CreateDirectory(directory); WorkspaceSnapshot.PrivateDirectory(directory);
        using var lease = new FileStream(Path.Combine(directory, ".coordinator.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        ProtectedIdentity? prior = resume && File.Exists(Path.Combine(directory, "protected-identity.json"))
            ? Json.Read<ProtectedIdentity>(await File.ReadAllTextAsync(Path.Combine(directory, "protected-identity.json"), token)) : null;
        if (resume && prior == null) throw new IOException("Legacy/agent-writable checkpoints cannot resume under protected execution; adopt policy and start a fresh run");
        var (revision, policy) = state.Policy(prior?.PolicyRevision);
        var snapshot = resume ? ProtectedState.ReadSnapshot(directory) : WorkspaceSnapshot.Capture(state.ProjectRoot);
        var launcher = new PodmanLauncher(policy.Runtime, snapshot, directory);
        var runtime = await launcher.VerifyAsync(token); // fail closed before any agent starts
        if (resume) await launcher.RecoverAsync(token);
        AgentCapabilities capabilities = new("scripted", "deterministic-1", StructuredClaims: true, Progress: false);
        if (policy.Backend != "scripted")
        {
            using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(token); probeTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            var probe = await launcher.ExecuteAsync(new(policy.AgentExecutable, ["--version"]), probeTimeout.Token);
            if (probe.ExitCode != 0 || probe.Stdout.Length is 0 or > 256) throw new IOException("Agent version probe failed");
            capabilities = new(policy.Backend, probe.Stdout.Trim());
        }
        string coordinator = Engine.Hash(Convert.ToBase64String(File.ReadAllBytes(typeof(ProtectedWorkflow).Assembly.Location)))
            + ":" + Engine.Hash(Convert.ToBase64String(File.ReadAllBytes(typeof(Engine).Assembly.Location)));
        var identity = new ProtectedIdentity(revision, runtime, capabilities, state.ProjectRoot, coordinator, policy.RepositoryService, policy.PipelineService);
        if (prior != null && Json.Serialize(prior) != Json.Serialize(identity)) throw new IOException("Backend/runtime/profile/policy/coordinator identity changed; resume refused");
        if (!resume)
        {
            await AtomicFile.WriteJsonAsync(Path.Combine(directory, "protected-identity.json"), identity, token);
            await AtomicFile.WriteJsonAsync(Path.Combine(directory, "approved-policy.json"), policy, token);
            await ProtectedState.SaveSnapshot(directory, snapshot);
        }
        ICodergenBackend backend = policy.Backend == "scripted" ? new ScriptedBackend()
            : new AgentBackend(policy.Backend is "codex" or "fake-codex"
                ? new CodexAgent(launcher, policy.AgentExecutable, capabilities.Version)
                : new ClaudeCodeAgent(launcher, policy.AgentExecutable, capabilities.Version));
        var registry = HandlerRegistry.Create(backend, interviewer, state.ProjectRoot);
        // These overrides are mandatory: no checkout-supplied process enters the native ToolHandler.
        registry.Register("tool", new DelegateHandler(async (request, ct) =>
        {
            var result = await launcher.ExecuteAsync(new("/bin/sh", ["-c", request.Node.Attributes.Text("tool_command")]), ct);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "stdout.txt"), result.Stdout, ct);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "stderr.txt"), result.Stderr, ct);
            await AtomicFile.WriteJsonAsync(Path.Combine(request.Directory, "execution-unit.json"), result.ExecutionId, ct);
            return result.ExitCode == 0 ? Outcome.Success("Isolated command exit zero; not a protected acceptance decision") : Outcome.Fail("Isolated command exited " + result.ExitCode);
        }));
        foreach (string type in new[] { "codergen", "tool" })
        {
            var underlying = registry.Resolve(new Node("wrapper", new Attributes { ["type"] = AttributeValue.Synthetic(type) }, new(0, 0)));
            registry.Register(type, new DelegateHandler(async (request, ct) =>
            {
                var outcome = await underlying.ExecuteAsync(request, ct);
                await ProtectedState.SaveSnapshot(directory, launcher.Snapshot);
                // Routing remains Appendix C; protected namespaces are never worker context.
                var updates = outcome.ContextUpdates.Where(p => !p.Key.StartsWith("verification", StringComparison.OrdinalIgnoreCase)
                    && !p.Key.StartsWith("acceptance", StringComparison.OrdinalIgnoreCase) && !p.Key.StartsWith("protected", StringComparison.OrdinalIgnoreCase)).ToDictionary();
                return outcome with { ContextUpdates = updates };
            }));
        }
        var prepared = Preparation.Prepare(policy.Workflow, registry); Preparation.RequireValid(prepared);
        if (policy.Backend != "scripted") foreach (var node in prepared.Graph.Nodes.Values.Where(n => n.Type == "codergen"))
        {
            var incoming = prepared.Graph.Edges.Where(e => e.To == node.Id).ToArray();
            if (incoming.Length == 0 ? Handoff.Resolve(prepared.Graph, node, null, "").Fidelity != "truncate"
                : incoming.Any(e => Handoff.Resolve(prepared.Graph, node, e, e.From).Fidelity != "truncate")) throw new ArgumentException("Coding agents support fresh truncate fidelity only");
            if (node.Attributes.Text("llm_provider").Length > 0) throw new ArgumentException("llm_provider is unsupported at the coding-agent seam; choose the agent in the approved policy");
        }
        options ??= new(policy.MaxVisits, policy.MaxSeconds);
        if (options.MaxVisits > policy.MaxVisits || options.MaxSeconds > policy.MaxSeconds) throw new ArgumentException("Run limits cannot exceed adopted policy limits");
        var settings = new ExecutionSettings(state.ProjectRoot, policy.Backend, null, runtime.Profile, policy.AllowModel,
            Path.Combine(state.ProjectDirectory, "runs"), Path.Combine(directory, "graph.dot"), [], new Dictionary<string, string> { ["policy"] = revision }, Limits: options);
        var engine = new Engine(registry, runStoreFactory: path => { state.RequireRun(path); return new RunStore(path); });
        engine.Event += evt => Event?.Invoke(evt);
        var pipeline = await engine.RunAsync(policy.Workflow, directory, state.ProjectRoot, options, resume, token, settings);
        // Candidate is a coordinator-captured immutable snapshot, never an assumed mutable HEAD.
        var candidate = new Candidate(new("local", Engine.Hash(state.ProjectRoot)[..24], "workspace"), "snapshot:" + launcher.Snapshot.Digest, launcher.Snapshot.Digest);
        await AtomicFile.WriteJsonAsync(Path.Combine(directory, "acceptance.json"), new AcceptanceDecision(false, "Verification pending"), CancellationToken.None);
        var evidence = new List<VerificationEvidence>();
        if (pipeline.State == "success")
        {
            foreach (var check in policy.Checks)
            {
                bool definitionsMatch = policy.PinnedFiles.All(pin => launcher.Snapshot.Files.Any(f => f.Path == pin.Path && Convert.ToBase64String(f.Bytes) == pin.Base64));
                string executionId = "definition-review-required"; CheckState result = CheckState.Failed;
                if (definitionsMatch)
                {
                    // Each verifier gets the exact candidate, freshly copied; verifier writes cannot alter it.
                    var verifier = new PodmanLauncher(policy.Runtime, launcher.Snapshot, directory);
                    var executed = await verifier.ExecuteAsync(new("/bin/sh", ["-c", check.Command]), token);
                    executionId = executed.ExecutionId; result = executed.ExitCode == 0 ? CheckState.Passed : CheckState.Failed;
                    string logs = Path.Combine(directory, "verification-logs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(logs);
                    await File.WriteAllTextAsync(Path.Combine(logs, "stdout.txt"), executed.Stdout, token);
                    await File.WriteAllTextAsync(Path.Combine(logs, "stderr.txt"), executed.Stderr, token);
                }
                var record = new VerificationEvidence(EvidenceSource.ControlledLocalCheck, candidate, revision, check.Id, result, executionId, DateTimeOffset.UtcNow);
                evidence.Add(record);
                await AtomicFile.WriteJsonAsync(Path.Combine(directory, "evidence", Guid.NewGuid().ToString("N") + ".json"), record, token);
            }
        }
        var acceptance = pipeline.State == "success" ? Acceptance.Decide(candidate, revision, policy.Checks.Select(c => c.Id), evidence)
            : new AcceptanceDecision(false, "Pipeline is incomplete/interrupted/failed; no code acceptance");
        await AtomicFile.WriteJsonAsync(Path.Combine(directory, "acceptance.json"), new { candidate, policy_revision = revision, decision = acceptance }, CancellationToken.None);
        return new(pipeline, candidate, acceptance, directory);
    }
}
