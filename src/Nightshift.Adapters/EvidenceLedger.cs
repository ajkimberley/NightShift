using Nightshift.Core;

namespace Nightshift.Adapters;

// Caller holds the coordinator lease. Ordering is durable sequence, never wall-clock time.
public static class EvidenceLedger
{
    public static IReadOnlyList<VerificationEvidence> Read(string run)
    {
        string root = Path.Combine(run, "evidence");
        if (!Directory.Exists(root)) return [];
        return Directory.GetFiles(root, "*.json").Order(StringComparer.Ordinal)
            .Where(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), "^[0-9]{8}\\.json$"))
            .Select(p => Json.Read<VerificationEvidence>(File.ReadAllText(p))).ToArray();
    }
    public static async Task AppendAsync(string run, VerificationEvidence evidence, CancellationToken token)
    {
        string root = Path.Combine(run, "evidence"); Directory.CreateDirectory(root);
        var files = Directory.GetFiles(root, "*.json").Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null && System.Text.RegularExpressions.Regex.IsMatch(n, "^[0-9]{8}$"));
        int ordinal = files.Select(n => int.Parse(n!)).DefaultIfEmpty(0).Max() + 1;
        string path = Path.Combine(root, ordinal.ToString("D8") + ".json");
        if (File.Exists(path)) throw new IOException("Evidence ordinal collision");
        await AtomicFile.WriteJsonAsync(path, evidence, token);
    }
}
