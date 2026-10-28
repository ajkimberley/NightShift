using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Nightshift.Core;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
public static class ProcessRunner
{
    // Full output is streamed to disk; the in-memory preview is bounded.
    private static async Task<string> CaptureAsync(StreamReader reader, string path, CancellationToken cancellationToken)
    {
        await using var output = new StreamWriter(path, false, new UTF8Encoding(false));
        var preview = new StringBuilder(); char[] buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count));
            if (preview.Length < 1024 * 1024) preview.Append(buffer, 0, Math.Min(count, 1024 * 1024 - preview.Length));
        }
        return preview.ToString();
    }
    public static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, string input, string artifactDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Directory.CreateDirectory(artifactDirectory);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["ATTRACTOR_STAGE_DIR"] = Path.GetFullPath(artifactDirectory);
        using var process = new Process { StartInfo = start };
        process.Start();
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "process.pid"), process.Id.ToString(), CancellationToken.None);
        var stdout = CaptureAsync(process.StandardOutput, Path.Combine(artifactDirectory, "stdout.txt"), cancellationToken);
        var stderr = CaptureAsync(process.StandardError, Path.Combine(artifactDirectory, "stderr.txt"), cancellationToken);
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        try
        {
            try { await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken); }
            catch (IOException) { /* Early process exit: exit code/output remain authoritative. */ }
            finally { process.StandardInput.Close(); }
            await process.WaitForExitAsync(cancellationToken);
            var result = new ProcessResult(process.ExitCode, await stdout, await stderr);
            cancellationToken.ThrowIfCancellationRequested(); return result;
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
        }
    }
}

public sealed record CodexProcessOptions(bool Enabled, string Executable, string WorkingDirectory, string Sandbox = "read-only");
public sealed class CodexProcessBackend(CodexProcessOptions options) : ICodergenBackend
{
    public BackendCapabilities Capabilities { get; } = new("codex-process", ["truncate"], Notes: "Fresh exec only; no session reuse, compaction, summaries or per-tool hooks. Model outcome is a claim requiring independent validation.");
    public async Task<BackendResult> RunAsync(StageRequest r, string prompt, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return new("", Outcome.Fail("Codex worker requires explicit opt-in"));
        if (options.Sandbox is not "read-only" and not "workspace-write") return new("", Outcome.Fail("Only read-only or workspace-write sandbox allowed"));
        if (r.Handoff.Fidelity != "truncate") return new("", Outcome.Fail("Codex process adapter only implements truncate fidelity"));
        if (r.Node.Attributes.Text("llm_provider") is { Length: > 0 } provider && provider != "openai") return new("", Outcome.Fail($"Provider '{provider}' not supported by this adapter"));
        string schema = Path.Combine(r.Directory, "outcome-schema.json"), last = Path.Combine(r.Directory, "last-message.json");
        await AtomicFile.WriteJsonAsync(schema, new
        {
            type = "object", properties = new { outcome = new { type = "string", @enum = new[] { "success", "partial_success", "fail", "retry" } }, notes = new { type = "string" } },
            required = new[] { "outcome", "notes" }, additionalProperties = false
        }, cancellationToken);
        var args = new List<string> { "exec", "--json", "--ephemeral", "--sandbox", options.Sandbox, "-C", options.WorkingDirectory, "--output-schema", schema, "--output-last-message", last };
        if (r.Node.Attributes.Text("llm_model") is { Length: > 0 } model) args.AddRange(["--model", model]);
        args.AddRange(["-c", "model_reasoning_effort=" + JsonSerializer.Serialize(r.Node.Attributes.Text("reasoning_effort", "high")), "-"]);
        // Truncate is intentionally minimal: do not smuggle full context into a fresh session.
        string input = $"Graph goal: {r.Graph.Attributes.Text("goal")}\nRun: {r.Context.Get("run.id")}\n\n{prompt}\n\nReturn the requested outcome JSON. Report incomplete work as fail or retry. An outcome is a worker claim, not independent test verification.";
        var process = await ProcessRunner.RunAsync(options.Executable, args, options.WorkingDirectory, input, r.Directory, cancellationToken);
        if (process.ExitCode != 0) return new(process.Stdout, Outcome.Fail($"Codex exited {process.ExitCode}"));
        var protocol = await CodexOutput.ReadAsync(Path.Combine(r.Directory, "stdout.txt"), last, cancellationToken);
        return new(process.Stdout, protocol);
    }
}
public static class CodexOutput
{
    public static async Task<Outcome> ReadAsync(string jsonlPath, string lastMessagePath, CancellationToken cancellationToken)
    {
        bool completed = false;
        try
        {
            using var reader = File.OpenText(jsonlPath);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line); string type = doc.RootElement.GetProperty("type").GetString() ?? "";
                if (type is "turn.failed" or "error") return Outcome.Fail("Codex event stream reported failure: " + line);
                if (type == "turn.completed") completed = true;
            }
            if (!completed) return Outcome.Fail("Codex has no turn.completed event; incomplete execution");
            if (!File.Exists(lastMessagePath)) return Outcome.Fail("Codex completed without structured outcome");
            var outcome = Json.Read<Outcome>(await File.ReadAllTextAsync(lastMessagePath, cancellationToken));
            if (outcome.Status == StageStatus.Skipped) return Outcome.Fail("Codex returned unsupported skipped status");
            return outcome with { Notes = "Worker claim, not independent verification. " + outcome.Notes };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return Outcome.Fail("Malformed Codex output: " + ex.Message); }
    }
}
