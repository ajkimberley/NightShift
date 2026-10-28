using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nightshift.Core;

public static class AtomicFile
{
    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(value));
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken); await stream.FlushAsync(cancellationToken); stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
public enum RunEventKind { PipelineStarted, PipelineResumed, PipelineCompleted, PipelineFailed, PipelineInterrupted, StageStarted, StageCompleted, StageFailed, StageRetrying, TransitionChosen, CheckpointSaved, InterviewStarted, InterviewCompleted, InterviewTimeout }
public sealed record RunEvent(RunEventKind Kind, string Node = "", int Visit = 0, int Attempt = 0, string Message = "", DateTimeOffset Timestamp = default);
public sealed record Transition(string From, string To, int? EdgeIndex, string Reason);
public sealed class Checkpoint
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset Timestamp { get; set; }
    public string GraphHash { get; set; } = "";
    public string CurrentNode { get; set; } = "";
    public string? NextNode { get; set; }
    public int? IncomingEdgeIndex { get; set; }
    public List<string> CompletedNodes { get; set; } = [];
    public Dictionary<string, Outcome> NodeOutcomes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> NodeVisits { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> NodeRetries { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonElement> ContextValues { get; set; } = new(StringComparer.Ordinal);
    public List<Transition> Transitions { get; set; } = [];
    public List<string> Logs { get; set; } = [];
    public int? PendingVisit { get; set; }
    public int AttemptOrdinal { get; set; }
    public int PendingRetries { get; set; }
    public int TotalVisits { get; set; }
    public int Revision { get; set; }
    public double ElapsedSeconds { get; set; }
    public string State { get; set; } = "running";
    public Outcome? FinalOutcome { get; set; }
}
public sealed record RunManifest(string GraphName, string Goal, string GraphHash, string WorkingDirectory, DateTimeOffset StartedAt, EngineOptions Options);
public sealed class RunStore : IDisposable
{
    public string Root { get; }
    private readonly FileStream lease;
    public event Action<RunEvent>? Event;
    public RunStore(string root)
    {
        Root = Path.GetFullPath(root); Directory.CreateDirectory(Root);
        lease = new(Path.Combine(Root, ".run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void Emit(RunEvent evt)
    {
        evt = evt with { Timestamp = DateTimeOffset.UtcNow };
        var options = new JsonSerializerOptions(Json.Options) { WriteIndented = false };
        File.AppendAllText(Path.Combine(Root, "events.jsonl"), JsonSerializer.Serialize(evt, options) + "\n");
        Event?.Invoke(evt);
    }
    public async Task SaveAsync(Checkpoint state)
    {
        state.Timestamp = DateTimeOffset.UtcNow; state.Revision++;
        await AtomicFile.WriteJsonAsync(Path.Combine(Root, "checkpoints", $"{state.Revision:D6}.json"), state);
        await AtomicFile.WriteJsonAsync(Path.Combine(Root, "checkpoint.json"), state);
        Emit(new(RunEventKind.CheckpointSaved, state.CurrentNode, Message: $"revision {state.Revision}; next={state.NextNode}"));
    }
    public Checkpoint Load() => Json.Read<Checkpoint>(File.ReadAllText(Path.Combine(Root, "checkpoint.json")));
    public string StageDirectory(string node, int visit, int attempt)
    {
        string path = Path.Combine(Root, node, $"visit-{visit:D4}", $"attempt-{attempt:D4}");
        // Checkpoint may lag an artifact write after a crash. Never overwrite prior evidence.
        while (Directory.Exists(path)) path = Path.Combine(Root, node, $"visit-{visit:D4}", $"attempt-{attempt:D4}-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path); return path;
    }
    public void Dispose() => lease.Dispose();
}

public sealed record ArtifactInfo(string Id, string Name, int SizeBytes, DateTimeOffset StoredAt, bool IsFileBacked);
public sealed class ArtifactStore(string? baseDirectory = null, int thresholdBytes = 100 * 1024)
{
    private readonly object sync = new();
    private readonly Dictionary<string, (ArtifactInfo Info, string Data)> artifacts = new(StringComparer.Ordinal);
    private string PathFor(string id) => Path.Combine(baseDirectory!, "artifacts", id + ".json");
    public ArtifactInfo Store<T>(string id, string name, T data)
    {
        if (!Regex.IsMatch(id, @"^[A-Za-z_][A-Za-z0-9_-]*$")) throw new ArgumentException("Artifact ID must be a safe identifier", nameof(id));
        string json = Json.Serialize(data); int size = Encoding.UTF8.GetByteCount(json);
        lock (sync)
        {
            bool backed = baseDirectory != null && size > thresholdBytes;
            if (artifacts.TryGetValue(id, out var previous) && previous.Info.IsFileBacked) File.Delete(PathFor(id));
            if (backed) AtomicFile.WriteJsonAsync(PathFor(id), data).GetAwaiter().GetResult();
            var info = new ArtifactInfo(id, name, size, DateTimeOffset.UtcNow, backed); artifacts[id] = (info, backed ? PathFor(id) : json); return info;
        }
    }
    public T Retrieve<T>(string id) { lock (sync) { var a = artifacts[id]; return Json.Read<T>(a.Info.IsFileBacked ? File.ReadAllText(a.Data) : a.Data); } }
    public bool Has(string id) { lock (sync) return artifacts.ContainsKey(id); }
    public IReadOnlyList<ArtifactInfo> List() { lock (sync) return artifacts.Values.Select(a => a.Info).ToList(); }
    public void Remove(string id) { lock (sync) if (artifacts.Remove(id, out var a) && a.Info.IsFileBacked) File.Delete(a.Data); }
    public void Clear() { lock (sync) foreach (string id in artifacts.Keys.ToArray()) Remove(id); }
}
