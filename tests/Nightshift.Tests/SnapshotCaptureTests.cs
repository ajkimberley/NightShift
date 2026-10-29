using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Nightshift.Adapters;
using Nightshift.Cli;
using Nightshift.Core;

namespace Nightshift.Tests;

public class SnapshotCaptureTests
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)] private static extern int MakeFifo(string path, uint mode);
    [Fact]
    public async Task Fifo_in_initial_project_fails_promptly_before_any_worker_starts()
    {
        string root = Path.Combine(Path.GetDirectoryName(ProtectedState.DefaultRoot)!, "snapshot-review-tests", Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
        Process? process = null;
        try
        {
            Assert.Equal(0, MakeFifo(Path.Combine(project, "named-pipe"), 0x180));
            var state = new ProtectedState(project, Path.Combine(root, "state"));
            await state.AdoptAsync(new("digraph fifo { start -> exit; }", "scripted", "/bin/sh", ContainerProfile.LocalDefault, [], []), CancellationToken.None);
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = project, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { typeof(CliApplication).Assembly.Location, "run", "--state-root", state.Root, "--max-seconds", "1" }) start.ArgumentList.Add(arg);
            process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            // Bound the child so a regression fails the test instead of hanging the runner.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, process.ExitCode); Assert.Contains("not a regular file", await stderr);
            Assert.DoesNotContain("Stage", await stdout);
            Assert.Empty(Directory.GetFiles(state.ProjectDirectory, "protected-identity.json", SearchOption.AllDirectories));
        }
        finally
        {
            if (process != null) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); process.Dispose(); }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact]
    public void Unix_socket_in_initial_project_is_refused_as_a_special_file()
    {
        using var w = new TestWorkspace(); using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(Path.Combine(w.Root, "socket")));
        Assert.Contains("not a regular file", Assert.Throws<IOException>(() => WorkspaceSnapshot.Capture(w.Root)).Message);
    }
    [Fact]
    public async Task Regular_files_preserve_bytes_and_executable_mode_with_bounded_capture()
    {
        using var w = new TestWorkspace(); string path = Path.Combine(w.Root, "script");
        await File.WriteAllBytesAsync(path, "#!/bin/sh\n"u8.ToArray());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var file = Assert.Single(WorkspaceSnapshot.Capture(w.Root).Files);
        Assert.Equal("#!/bin/sh\n"u8.ToArray(), file.Bytes); Assert.True(file.Executable);
        await using (var oversized = File.OpenWrite(path)) oversized.SetLength(WorkspaceSnapshot.MaximumBytes + 1L);
        Assert.Throws<IOException>(() => WorkspaceSnapshot.Capture(w.Root));
    }
    [Fact]
    public void Cancelled_capture_and_symlink_inputs_are_refused()
    {
        using var w = new TestWorkspace();
        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceSnapshot.Capture(w.Root, new CancellationToken(true)));
        File.CreateSymbolicLink(Path.Combine(w.Root, "link"), "/etc/passwd");
        Assert.Throws<IOException>(() => WorkspaceSnapshot.Capture(w.Root));
    }
}
