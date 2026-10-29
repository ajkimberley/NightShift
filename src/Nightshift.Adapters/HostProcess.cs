using System.Diagnostics;
using System.Text;

namespace Nightshift.Adapters;

// Coordinator utility only. Never used to execute checkout-supplied code on the host.
internal static class HostProcess
{
    internal sealed record Result(int ExitCode, byte[] Output, string Error)
    {
        public string Text => Encoding.UTF8.GetString(Output);
    }
    internal static async Task<Result> Run(string executable, IEnumerable<string> args, string input,
        CancellationToken token, int maxBytes = 32 * 1024 * 1024, string? tracePrefix = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetTempPath() };
        start.Environment.Clear();
        foreach (string key in new[] { "HOME", "USER", "TMPDIR" })
            if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        start.Environment["PATH"] = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var p = Process.Start(start) ?? throw new IOException("Could not launch coordinator utility");
        using var drain = new CancellationTokenSource();
        async Task<byte[]> Read(Stream stream, int bound, string suffix)
        {
            await using var audit = tracePrefix == null ? null : new FileStream(tracePrefix + suffix, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var bytes = new MemoryStream(); byte[] buffer = new byte[8192]; int n;
            while ((n = await stream.ReadAsync(buffer, drain.Token)) > 0)
            {
                if (bytes.Length + n > bound) { try { p.Kill(true); } catch (InvalidOperationException) { } throw new IOException("Process output limit exceeded"); }
                bytes.Write(buffer, 0, n); if (audit != null) { await audit.WriteAsync(buffer.AsMemory(0, n), drain.Token); await audit.FlushAsync(drain.Token); }
            }
            return bytes.ToArray();
        }
        var stdout = Read(p.StandardOutput.BaseStream, maxBytes, ".stdout");
        var stderr = Read(p.StandardError.BaseStream, 1024 * 1024, ".stderr");
        using var registration = token.Register(() => { try { p.Kill(true); } catch (InvalidOperationException) { } drain.CancelAfter(TimeSpan.FromSeconds(1)); });
        try
        {
            try { await p.StandardInput.WriteAsync(input.AsMemory(), token); } catch (IOException) { }
            p.StandardInput.Close();
            await p.WaitForExitAsync(token);
            drain.CancelAfter(TimeSpan.FromSeconds(2));
            return new(p.ExitCode, await stdout, Encoding.UTF8.GetString(await stderr));
        }
        finally
        {
            if (!p.HasExited) { try { p.Kill(true); } catch (InvalidOperationException) { } }
            drain.CancelAfter(TimeSpan.FromSeconds(1));
            try { await Task.WhenAll(stdout, stderr); } catch (Exception e) when (e is OperationCanceledException or IOException) { }
        }
    }
}
