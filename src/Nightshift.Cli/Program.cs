using System.Globalization;
using Nightshift.Core;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "help" or "--help" or "-h") { Help(); return 0; }
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    try
    {
        string command = args[0];
        if (command is not "validate" and not "run" and not "resume" and not "inspect" and not "events") throw new ArgumentException($"Unknown command '{command}'");
        if (args.Length < 2) throw new ArgumentException("Expected graph file or run directory");
        var values = new Dictionary<string, string>(StringComparer.Ordinal); bool enableCodex = false;
        var allowed = new[] { "--dir", "--answers", "--backend", "--codex", "--sandbox", "--max-visits", "--max-seconds", "--pause-after" };
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--enable-codex") { enableCodex = true; continue; }
            if (!allowed.Contains(args[i]) || i + 1 >= args.Length) throw new ArgumentException($"Unknown or missing option '{args[i]}'");
            if (!values.TryAdd(args[i], args[++i])) throw new ArgumentException("Duplicate option");
        }
        string target = Path.GetFullPath(args[1]);
        if (command is "inspect" or "events")
        {
            Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(target, command == "inspect" ? "checkpoint.json" : "events.jsonl"), cancellation.Token)); return 0;
        }
        bool resume = command == "resume";
        string directory = resume ? target : Path.GetFullPath(values.GetValueOrDefault("--dir", Path.Combine("runs", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6])));
        var manifest = resume ? Json.Read<RunManifest>(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json"), cancellation.Token)) : null;
        string workdir = manifest?.WorkingDirectory ?? Environment.CurrentDirectory;
        ICodergenBackend backend = values.GetValueOrDefault("--backend", "scripted") switch
        {
            "scripted" => new ScriptedBackend(),
            "codex" when enableCodex => new CodexProcessBackend(new(true, values.GetValueOrDefault("--codex", "codex"), workdir, values.GetValueOrDefault("--sandbox", "read-only"))),
            "codex" => throw new ArgumentException("Real workers require --backend codex --enable-codex; this may spend money. Use scripted for demos."),
            var name => throw new ArgumentException($"Unknown backend '{name}'")
        };
        IInterviewer interviewer = values.TryGetValue("--answers", out string? answers) ? new QueueInterviewer(answers.Split(',').Select(a => new Answer(a.Trim(), Identity: "cli-queue-unverified"))) : new ConsoleInterviewer();
        var registry = HandlerRegistry.Create(backend, interviewer, workdir);
        string dot = await File.ReadAllTextAsync(resume ? Path.Combine(directory, "graph.dot") : target, cancellation.Token);
        var prepared = Preparation.Prepare(dot, registry);
        foreach (var d in prepared.Diagnostics) Console.Error.WriteLine($"{d.Severity} {d.Rule} {d.NodeId ?? d.Edge} {d.Location}: {d.Message}");
        Preparation.RequireValid(prepared);
        if (command == "validate")
        {
            Console.WriteLine($"Valid: {prepared.Graph.Nodes.Count} nodes, {prepared.Graph.Edges.Count} edges");
            foreach (var node in prepared.Graph.Nodes.Values.Where(n => n.Type == "codergen")) Console.WriteLine($"  {node.Id}: model={node.Attributes.Text("llm_model", "(backend default)")} sources={Json.Serialize(node.ConfigurationSources)}");
            return 0;
        }
        var prior = manifest?.Options ?? new EngineOptions();
        var options = new EngineOptions(
            values.TryGetValue("--max-visits", out var visits) ? int.Parse(visits, CultureInfo.InvariantCulture) : prior.MaxVisits,
            values.TryGetValue("--max-seconds", out var seconds) ? double.Parse(seconds, CultureInfo.InvariantCulture) : prior.MaxSeconds,
            values.TryGetValue("--pause-after", out var stages) ? int.Parse(stages, CultureInfo.InvariantCulture) : null);
        Console.WriteLine($"Backend: {backend.Capabilities.Name}. Run directory: {directory}");
        var result = await new Engine(registry).RunAsync(dot, directory, workdir, options, resume, cancellation.Token);
        Console.WriteLine($"{result.State}: {result.Outcome.Notes}{result.Outcome.FailureReason}");
        Console.WriteLine("Trace: " + string.Join(" -> ", result.Checkpoint.CompletedNodes));
        return result.State == "interrupted" ? 3 : result.Outcome.IsSuccess ? 0 : 1;
    }
    catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled"); return 130; }
    catch (Exception e) when (e is ArgumentException or IOException or FormatException or PipelineException or DotParseException or System.Text.Json.JsonException)
    { Console.Error.WriteLine(e.Message); return 2; }
}
static void Help() => Console.WriteLine("""
Nightshift — a partial Attractor implementation (.NET 10)
  validate GRAPH.dot
  run GRAPH.dot [--dir RUN_DIR] [--answers A,R,A] [--pause-after N]
  resume RUN_DIR [--answers A] [--pause-after N]
  inspect RUN_DIR
  events RUN_DIR

Run/resume options: --max-visits N (default 1000), --max-seconds N (default 3600)
The default scripted backend simulates outcomes without credentials or network.
Human gates use the console unless --answers is supplied (empty queue => skipped).
Pause exits 3; failed/incomplete run exits 1; invalid input exits 2; success exits 0.
Resume retains completed evidence and may replay an interrupted stage's effects.

Explicit worker opt-in (may incur charges; not used by demos):
  --backend codex --enable-codex [--codex EXECUTABLE] [--sandbox read-only|workspace-write]
Codex requires truncate fidelity; no session reuse, summaries or per-tool hooks.
Each resume requires worker opt-in again; no worker credentials are saved.
""");
