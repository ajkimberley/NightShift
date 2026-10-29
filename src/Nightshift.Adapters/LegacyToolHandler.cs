using Nightshift.Core;
namespace Nightshift.Adapters;

// Explicit native-development compatibility only. Never composed by protected execution.
public sealed class ToolHandler(string workingDirectory) : IHandler
{
    public async Task<Outcome> ExecuteAsync(StageRequest r, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", r.Node.Attributes.Text("tool_command")], workingDirectory, "", r.Directory, cancellationToken);
        string statusFile = Path.Combine(r.Directory, "worker-status.json");
        if (result.ExitCode != 0) return Outcome.Fail($"Command exited {result.ExitCode}; see stderr.txt");
        if (File.Exists(statusFile)) return Json.Read<Outcome>(await File.ReadAllTextAsync(statusFile, cancellationToken)) with { Notes = "Legacy untrusted worker status claim; not verified acceptance" };
        var updates = new RunContext(); updates.Set("tool.output", result.Stdout); updates.Set("tool.exit_code", result.ExitCode);
        return new() { Status = StageStatus.Success, ContextUpdates = updates.Snapshot(), Notes = "Command exited zero; domain correctness requires explicit checks" };
    }
}
