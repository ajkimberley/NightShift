using System.Diagnostics;
using System.Text.RegularExpressions;
using Nightshift.Core;

namespace Nightshift.Cli;

public sealed class ProgressDisplay : IAsyncDisposable
{
    private readonly TextWriter writer;
    private readonly object sync = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task heartbeat;
    private string? stage; private int visit, attempt; private Stopwatch? elapsed;
    public ProgressDisplay(TextWriter writer, TimeSpan? interval = null)
    {
        this.writer = writer;
        heartbeat = Heartbeat(interval ?? TimeSpan.FromSeconds(5));
    }
    public void Handle(RunEvent e)
    {
        lock (sync)
        {
            switch (e.Kind)
            {
                case RunEventKind.StageStarted:
                    stage = e.Node; visit = e.Visit; attempt = e.Attempt; elapsed = Stopwatch.StartNew();
                    writer.WriteLine($"Stage {stage}: running | visit {visit}, attempt {attempt}"); break;
                case RunEventKind.StageCompleted:
                case RunEventKind.StageFailed:
                    writer.WriteLine($"Stage {e.Node}: {(e.Kind == RunEventKind.StageCompleted ? "completed" : "failed/requested retry")} | visit {e.Visit}, attempt {e.Attempt} | {elapsed?.Elapsed.TotalSeconds ?? 0:F1}s | {Clean(e.Message)}"); stage = null; break;
                case RunEventKind.StageRetrying: writer.WriteLine($"Stage {e.Node}: retry scheduled | {Clean(e.Message)}"); break;
                case RunEventKind.InterviewStarted: writer.WriteLine($"Stage {e.Node}: waiting for a human answer"); break;
                case RunEventKind.InterviewCompleted:
                case RunEventKind.InterviewTimeout: writer.WriteLine($"Stage {e.Node}: {e.Kind} | {Clean(e.Message)}"); break;
            }
        }
    }
    private async Task Heartbeat(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token)) lock (sync) if (stage != null) writer.WriteLine($"Stage {stage}: still running | visit {visit}, attempt {attempt} | {elapsed!.Elapsed.TotalSeconds:F0}s elapsed (worker logs captured on disk)");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); await heartbeat; stop.Dispose(); }
    public static string Clean(string text) => Regex.Replace(Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]", ""), @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
    public static void Print(RunView view, TextWriter writer, bool includeResponse)
    {
        writer.WriteLine($"{view.State}: {Clean(view.Summary)}");
        writer.WriteLine($"Workspace: {view.WorkingDirectory}"); writer.WriteLine($"Backend: {view.Backend} | Sandbox: {view.Sandbox}");
        writer.WriteLine($"Stages visited: {view.Visits} | Checkpointed active time: {view.ActiveSeconds:F1}s");
        if (view.NextNode != null) writer.WriteLine($"Next stage: {view.NextNode}");
        foreach (string verdict in view.ToolVerdicts) writer.WriteLine("Tool verdict: " + verdict);
        foreach (var output in view.Outputs) writer.WriteLine($"Declared output ({(output.Exists ? "exists; existence alone is not verification" : "missing")}): {output.Path}");
        if (includeResponse && view.Response != null)
        {
            string text;
            if (view.Response.EndsWith("last-message.json", StringComparison.Ordinal))
            {
                try { text = Json.Read<Outcome>(File.ReadAllText(view.Response)).Notes; }
                catch (System.Text.Json.JsonException) { text = "Final worker response is malformed; inspect its file."; }
            }
            else { using var reader = File.OpenText(view.Response); char[] chars = new char[1200]; text = new(chars, 0, reader.Read(chars, 0, chars.Length)); }
            writer.WriteLine("Worker response (a claim; check tool verdicts separately):\n" + Clean(text.Length > 1200 ? text[..1200] + "…" : text));
        }
        writer.WriteLine($"Run: {view.RunDirectory}"); writer.WriteLine($"Checkpoint: {view.Checkpoint}"); writer.WriteLine($"Events: {view.Events}");
        if (view.Response != null) writer.WriteLine($"Response: {view.Response}");
        if (view.Logs != null) writer.WriteLine($"Stage logs: {view.Logs}");
    }
}
