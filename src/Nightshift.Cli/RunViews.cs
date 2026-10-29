using System.Text.Json;
using Nightshift.Core;

namespace Nightshift.Cli;

public sealed record OutputPath(string Path, bool Exists);
public sealed record RunView(string RunDirectory, string State, string WorkingDirectory, string Backend, string Sandbox, string? CurrentNode, string? NextNode, int Visits, double ActiveSeconds, string Summary, string Checkpoint, string Events, string? Response, string? Logs, IReadOnlyList<OutputPath> Outputs, IReadOnlyList<string> ToolVerdicts);
public static class RunViews
{
    public static ExecutionSettings? ReadExecution(string directory, RunManifest manifest) => File.Exists(Path.Combine(directory, "execution.json")) ? Json.Read<ExecutionSettings>(File.ReadAllText(Path.Combine(directory, "execution.json"))) : manifest.Execution;
    public static RunView Read(string directory)
    {
        directory = Path.GetFullPath(directory);
        var manifest = Json.Read<RunManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var execution = ReadExecution(directory, manifest);
        var state = Json.Read<Checkpoint>(File.ReadAllText(Path.Combine(directory, "checkpoint.json")));
        string? response = null, logs = null;
        var order = (state.NextNode == null ? state.CompletedNodes : state.CompletedNodes.Append(state.NextNode)).Reverse().Distinct();
        foreach (string node in order)
        {
            string nodeRoot = Path.Combine(directory, node);
            if (!Directory.Exists(nodeRoot)) continue;
            foreach (string visit in Directory.GetDirectories(nodeRoot).OrderDescending(StringComparer.Ordinal))
            foreach (string attempt in Directory.GetDirectories(visit).OrderDescending(StringComparer.Ordinal))
            {
                if (logs == null && (File.Exists(Path.Combine(attempt, "stdout.txt")) || File.Exists(Path.Combine(attempt, "prompt.md")))) logs = attempt;
                string candidate = Path.Combine(attempt, "last-message.json");
                if (response == null && File.Exists(candidate)) response = candidate;
                if (response == null && File.Exists(Path.Combine(attempt, "response.md"))) response = Path.Combine(attempt, "response.md");
            }
        }
        string summary = state.FinalOutcome?.FailureReason is { Length: > 0 } reason ? reason : state.FinalOutcome?.Notes ?? (state.State == "interrupted" ? "Ready to resume; an interrupted effect may replay." : "Execution is pending or active.");
        if (!File.Exists(Path.Combine(directory, "protected-identity.json"))) summary = "[Legacy untrusted evidence; no verified acceptance] " + summary;
        var verdicts = new List<string>();
        var graph = new DotParser().Parse(File.ReadAllText(Path.Combine(directory, "graph.dot")));
        foreach (var (id, outcome) in state.NodeOutcomes)
        {
            if (graph.Nodes.TryGetValue(id, out var n) && n.Type == "tool") verdicts.Add($"{id}: {Conditions.StatusText(outcome.Status)}" + (File.Exists(Path.Combine(directory, "protected-identity.json")) ? " (isolated command; acceptance separate)" : " (untrusted native command claim)") + (n.Attributes.Bool("goal_gate") ? " (Attractor goal gate)" : ""));
        }
        return new(directory, state.State, manifest.WorkingDirectory, execution?.Backend ?? "legacy: not recorded", execution?.Sandbox ?? "legacy: not recorded", state.CurrentNode, state.NextNode, state.TotalVisits, state.ElapsedSeconds, summary, Path.Combine(directory, "checkpoint.json"), Path.Combine(directory, "events.jsonl"), response, logs,
            (execution?.Outputs ?? []).Select(p => new OutputPath(p, File.Exists(p) || Directory.Exists(p))).ToList(), verdicts);
    }
    public static string Latest(Project project, string outputRoot, string workingDirectory, bool resumable)
    {
        var candidates = new List<(string Path, DateTimeOffset Started)>();
        if (Directory.Exists(outputRoot)) foreach (string dir in Directory.GetDirectories(outputRoot))
        {
            try
            {
                var manifest = Json.Read<RunManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json")));
                var checkpoint = Json.Read<Checkpoint>(File.ReadAllText(Path.Combine(dir, "checkpoint.json")));
                if (manifest.Execution is { } settings ? Path.GetFullPath(settings.ProjectRoot) != project.Root : Path.GetFullPath(manifest.WorkingDirectory) != workingDirectory) continue;
                if (resumable && (checkpoint.FinalOutcome != null || checkpoint.State is not "interrupted" and not "running" || IsActive(dir))) continue;
                candidates.Add((dir, manifest.StartedAt));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { /* Incomplete/unrelated folders are not runs. */ }
        }
        return candidates.OrderByDescending(c => c.Started).ThenBy(c => c.Path, StringComparer.Ordinal).FirstOrDefault().Path ?? throw new ArgumentException(resumable ? "No resumable interrupted run in this project. Use nightshift status --latest to inspect its most recent run." : "No runs found for this project. Run nightshift demo or nightshift run first.");
    }
    private static bool IsActive(string directory)
    {
        string path = Path.Combine(directory, ".run.lock");
        if (!File.Exists(path)) return false;
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { return true; }
    }
}
