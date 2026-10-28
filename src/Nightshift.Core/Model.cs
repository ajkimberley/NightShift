using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nightshift.Core;

public sealed record SourceLocation(int Line, int Column);
public enum ValueKind { String, Integer, Float, Boolean, Duration, Bare }
public sealed record AttributeValue(string Text, ValueKind Kind, SourceLocation Location)
{
    public static AttributeValue Synthetic(string text) => new(text, ValueKind.String, new(0, 0));
}

public sealed class Attributes : Dictionary<string, AttributeValue>
{
    public Attributes() : base(StringComparer.Ordinal) { }
    public Attributes(IEnumerable<KeyValuePair<string, AttributeValue>> values) : base(values, StringComparer.Ordinal) { }
    public string Text(string key, string fallback = "") => TryGetValue(key, out var v) ? v.Text : fallback;
    public bool Bool(string key) => Text(key) == "true";
    public int Int(string key, int fallback = 0) => TryGetValue(key, out var v) ? int.Parse(v.Text, CultureInfo.InvariantCulture) : fallback;
    public void Merge(Attributes other) { foreach (var (k, v) in other) this[k] = v; }
    public void Set(string key, string value) => this[key] = AttributeValue.Synthetic(value);
}

public sealed class Node(string id, Attributes attributes, SourceLocation location)
{
    public string Id { get; } = id;
    public Attributes Attributes { get; } = attributes;
    public SourceLocation Location { get; } = location;
    public HashSet<string> ExplicitKeys { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Classes { get; } = new(StringComparer.Ordinal);
    public string SubgraphThread { get; set; } = "";
    public Dictionary<string, string> ConfigurationSources { get; } = new(StringComparer.Ordinal);
    public string Label => Attributes.Text("label", Id);
    public string Shape => Attributes.Text("shape", "box");
    public string Type => Attributes.Text("type") is { Length: > 0 } t ? t : ShapeTypes.GetValueOrDefault(Shape, "codergen");
    public bool IsStart => Type == "start" || Id is "start" or "Start";
    public bool IsExit => Type == "exit" || Id is "exit" or "end";
    public static readonly IReadOnlyDictionary<string, string> ShapeTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Mdiamond"] = "start", ["Msquare"] = "exit", ["box"] = "codergen", ["hexagon"] = "wait.human",
        ["diamond"] = "conditional", ["component"] = "parallel", ["tripleoctagon"] = "parallel.fan_in",
        ["parallelogram"] = "tool", ["house"] = "stack.manager_loop"
    };
}
public sealed record Edge(string From, string To, Attributes Attributes, SourceLocation Location)
{
    public string Condition => Attributes.Text("condition");
}
public sealed class Graph(string id)
{
    public string Id { get; } = id;
    public Attributes Attributes { get; } = new();
    public Dictionary<string, Node> Nodes { get; } = new(StringComparer.Ordinal);
    public List<Edge> Edges { get; } = [];
    public IEnumerable<Edge> Outgoing(string id) => Edges.Where(e => e.From == id);
}

public enum StageStatus { Success, Fail, PartialSuccess, Retry, Skipped }
public sealed record Outcome
{
    // Appendix C uses outcome, not the internal field name status.
    [JsonPropertyName("outcome"), JsonRequired] public StageStatus Status { get; init; }
    public string PreferredLabel { get; init; } = "";
    public List<string> SuggestedNextIds { get; init; } = [];
    public Dictionary<string, JsonElement> ContextUpdates { get; init; } = new(StringComparer.Ordinal);
    public string Notes { get; init; } = "";
    public string FailureReason { get; init; } = "";
    [JsonIgnore] public bool IsSuccess => Status is StageStatus.Success or StageStatus.PartialSuccess;
    public static Outcome Success(string notes = "") => new() { Status = StageStatus.Success, Notes = notes };
    public static Outcome Fail(string reason) => new() { Status = StageStatus.Fail, FailureReason = reason };
}

public sealed class RunContext
{
    private readonly object sync = new();
    private readonly Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
    public void Set<T>(string key, T value) { lock (sync) values[key] = JsonSerializer.SerializeToElement(value, Json.Options); }
    public string Get(string key, string fallback = "")
    {
        lock (sync) return values.TryGetValue(key, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText() : fallback;
    }
    public bool Has(string key) { lock (sync) return values.ContainsKey(key); }
    public void Apply(IEnumerable<KeyValuePair<string, JsonElement>> updates) { lock (sync) foreach (var (k, v) in updates) values[k] = v.Clone(); }
    public Dictionary<string, JsonElement> Snapshot() { lock (sync) return values.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal); }
    public RunContext Clone() { var c = new RunContext(); c.Apply(Snapshot()); return c; }
}

public enum Severity { Info, Warning, Error }
public sealed record Diagnostic(string Rule, Severity Severity, string Message, string? NodeId = null, string? Edge = null, SourceLocation? Location = null, string? Fix = null);
public sealed class PipelineException(string message) : Exception(message);
public sealed class TransientException(string message) : Exception(message);
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string text) => JsonSerializer.Deserialize<T>(text, Options) ?? throw new JsonException("Null document");
}
public static class Durations
{
    public static TimeSpan Parse(string text)
    {
        var m = Regex.Match(text, @"^(-?\d+)(ms|s|m|h|d)$", RegexOptions.CultureInvariant);
        if (!m.Success) throw new FormatException($"Invalid duration '{text}'");
        double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (n < 0) throw new FormatException("Duration cannot be negative");
        return TimeSpan.FromMilliseconds(n * (m.Groups[2].Value switch { "ms" => 1, "s" => 1000, "m" => 60000, "h" => 3600000, _ => 86400000 }));
    }
}
