using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Nightshift.Cli;

public sealed record ExecutableResolution(string Path, string Source);
public sealed record DiagnosticCheck(string Name, string State, string Detail, string? NextAction = null);
public sealed record DoctorReport(string Version, string Runtime, string Installation, string Project, string? Configuration, string WorkingDirectory, string OutputRoot, string Backend, string Sandbox, bool RealWorkersEnabled, ExecutableResolution? Codex, IReadOnlyList<DiagnosticCheck> Checks)
{
    public bool Healthy => Checks.All(c => c.State != "error");
}
public static class CodexDiscovery
{
    public const string MacBundle = "/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex";
    public static bool IsExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || Directory.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return true;
        return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
    public static void RequireExecutable(string path)
    {
        if (!IsExecutable(path)) throw new ArgumentException($"Codex path is not an executable file: '{path}'. Supply --codex /absolute/path/to/codex or correct codex_executable in nightshift.json.");
    }
    public static ExecutableResolution Resolve(string? cliPath, string? configuredPath, string invocation, string projectRoot, string? searchPath = null, string? bundledPath = null)
    {
        if (cliPath != null) { string p = Path.GetFullPath(cliPath, invocation); RequireExecutable(p); return new(p, "command line"); }
        if (configuredPath != null) { string p = Path.GetFullPath(configuredPath, projectRoot); RequireExecutable(p); return new(p, "project configuration"); }
        foreach (string directory in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.GetFullPath(Path.Combine(directory, OperatingSystem.IsWindows() ? "codex.exe" : "codex"), invocation);
            if (IsExecutable(candidate)) return new(candidate, "PATH");
        }
        string bundle = bundledPath ?? (OperatingSystem.IsMacOS() ? MacBundle : "");
        if (IsExecutable(bundle)) return new(bundle, "macOS application bundle");
        throw new ArgumentException("Codex executable was not found. Supply --codex /absolute/path/to/codex, set codex_executable, or install the Codex CLI yourself. The offline demo requires no Codex.");
    }
    private sealed record Probe(int ExitCode, string Output);
    private static async Task<Probe> ProbeAsync(string executable, string[] args, TimeSpan timeout, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(timeout);
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start diagnostic process");
        process.StandardInput.Close();
        async Task<string> Drain(StreamReader reader)
        {
            char[] buffer = new char[1024]; var preview = new StringBuilder(); int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), bounded.Token)) > 0) if (preview.Length < 65536) preview.Append(buffer, 0, Math.Min(count, 65536 - preview.Length));
            return preview.ToString();
        }
        var output = Drain(process.StandardOutput); var error = Drain(process.StandardError);
        using var registration = bounded.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        try { await process.WaitForExitAsync(bounded.Token); return new(process.ExitCode, await output); }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) when (bounded.IsCancellationRequested) { }
        }
    }
    public static async Task<IReadOnlyList<DiagnosticCheck>> CheckAsync(ExecutableResolution executable, bool required, TimeSpan timeout, CancellationToken token)
    {
        var checks = new List<DiagnosticCheck>(); string failure = required ? "error" : "warning";
        try
        {
            var version = await ProbeAsync(executable.Path, ["--version"], timeout, token);
            // Never echo arbitrary diagnostic output: even an unexpected CLI must not leak secrets.
            string safeVersion = Regex.Match(version.Output, @"\bcodex-cli\s+[0-9]+\.[0-9]+\.[0-9]+(?:[-.+][A-Za-z0-9.-]+)?").Value;
            checks.Add(new("Codex version", version.ExitCode == 0 && safeVersion.Length > 0 ? "ok" : failure, safeVersion.Length > 0 ? safeVersion : "Could not recognize Codex version", "Use a compatible Codex CLI executable."));
            var help = await ProbeAsync(executable.Path, ["exec", "--help"], timeout, token);
            string[] requiredFlags = ["--json", "--ephemeral", "--sandbox", "--output-schema", "--output-last-message", "--cd", "--config", "--model"];
            var missing = requiredFlags.Where(flag => !help.Output.Contains(flag, StringComparison.Ordinal)).ToList();
            checks.Add(new("Codex interface", help.ExitCode == 0 && missing.Count == 0 ? "ok" : failure, missing.Count == 0 && help.ExitCode == 0 ? "Required exec flags available" : "Missing/unsupported exec flags: " + string.Join(", ", missing), "Compare codex exec --help with the adapter requirements."));
            var auth = await ProbeAsync(executable.Path, ["login", "status"], timeout, token);
            checks.Add(new("Stored authentication", auth.ExitCode == 0 ? "ok" : failure, auth.ExitCode == 0 ? "Credentials reported present; live model access is unverified" : "CLI reports no usable stored authentication", "Run codex login yourself if needed; doctor never logs in."));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { checks.Add(new("Codex diagnostics", failure, "Diagnostic subprocess timed out", "Check the executable or try doctor again after fixing the stalled CLI.")); }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { checks.Add(new("Codex diagnostics", failure, "Could not query executable (" + e.GetType().Name + ")", "Check that the selected executable can be run by your account.")); }
        return checks;
    }
}
