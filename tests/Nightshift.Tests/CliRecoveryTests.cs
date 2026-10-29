using System.Diagnostics;
using System.Text.Json;
using Nightshift.Core;

namespace Nightshift.Tests;

public class CliRecoveryTests
{
    private static readonly string Repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static readonly string Configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
    private static readonly string Cli = Path.Combine(Repository, "src", "Nightshift.Cli", "bin", Configuration, "net10.0", "Nightshift.Cli.dll");
    private static Process Start(string workdir, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workdir, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Cli); foreach (var a in args) start.ArgumentList.Add(a); start.ArgumentList.Add("--profile"); start.ArgumentList.Add("native-development"); return Process.Start(start)!;
    }
    [Fact]
    public async Task AbruptProcessInterruptionBeforeCompletionResumesFromDurableContinuation()
    {
        using var w = new TestWorkspace(); string dot = Path.Combine(w.Root, "pipeline.dot"); string run = w.RunDirectory();
        await File.WriteAllTextAsync(dot, """
        digraph G {
          start [shape=Mdiamond]
          first [prompt=first, goal_gate=true]
          work [type=tool, tool_command="if [ -f effect.marker ]; then echo replay; else echo effect > effect.marker; sleep 30; fi"]
          exit [shape=Msquare]
          start -> first -> work -> exit
        }
        """);
        using (var process = Start(w.Root, "run", dot, "--dir", run))
        {
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await ProcessTests.WaitForFile(Path.Combine(w.Root, "effect.marker")); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); }
        }
        string checkpointPath = Path.Combine(run, "checkpoint.json");
        var checkpoint = Json.Read<Checkpoint>(await File.ReadAllTextAsync(checkpointPath));
        Assert.Equal("first", checkpoint.CurrentNode); Assert.Equal("work", checkpoint.NextNode); Assert.Equal(StageStatus.Success, checkpoint.NodeOutcomes["first"].Status);
        string firstStatus = Directory.GetFiles(Path.Combine(run, "first"), "status.json", SearchOption.AllDirectories).Single(); string original = await File.ReadAllTextAsync(firstStatus);
        using var resumed = Start(w.Root, "resume", run); var output = resumed.StandardOutput.ReadToEndAsync(); var error = resumed.StandardError.ReadToEndAsync();
        await resumed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(resumed.ExitCode == 0, await output + await error);
        var final = Json.Read<Checkpoint>(await File.ReadAllTextAsync(checkpointPath)); Assert.Equal("success", final.State);
        Assert.Equal(new[] { "start", "first", "work", "exit" }, final.CompletedNodes); Assert.Equal(1, final.NodeVisits["work"]);
        Assert.Equal(original, await File.ReadAllTextAsync(firstStatus)); Assert.Equal(2, Directory.GetDirectories(Path.Combine(run, "work", "visit-0001")).Length);
        Assert.True(Directory.GetFiles(Path.Combine(run, "checkpoints")).Length > 3);
    }
    [Fact]
    public async Task CliPauseResumeInspectEventsValidationAndWorkerOptInHaveExplicitExitCodes()
    {
        using var w = new TestWorkspace(); string graph = Path.Combine(w.Root, "g.dot"); await File.WriteAllTextAsync(graph, EngineTests.StageGraph());
        async Task<(int Code, string Output)> Invoke(params string[] args)
        {
            using var p = Start(w.Root, args); var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); return (p.ExitCode, await stdout + await stderr);
        }
        Assert.Equal(0, (await Invoke("validate", graph)).Code);
        Assert.Equal(3, (await Invoke("run", graph, "--dir", w.RunDirectory(), "--pause-after", "2")).Code);
        Assert.Equal(0, (await Invoke("resume", w.RunDirectory())).Code);
        Assert.Contains("success", (await Invoke("inspect", w.RunDirectory())).Output);
        Assert.Contains("pipeline_completed", (await Invoke("events", w.RunDirectory())).Output);
        Assert.Equal(2, (await Invoke("run", graph, "--backend", "codex")).Code);
        Assert.Equal(2, (await Invoke("run", graph, "--typo", "x")).Code);
    }
}
