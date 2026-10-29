using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nightshift.Core;

namespace Nightshift.Adapters;

public sealed record ContainerProfile(string Executable, string Connection, string ShareRoot, string Image)
{
    public const string OfficialImage = "docker.io/library/alpine@sha256:2e0b81d60597f449baa56dc25d355367869d2dca4cd57e866f1e2b52af3eb55f";
    public static ContainerProfile LocalDefault => new(OperatingSystem.IsMacOS() ? "/opt/homebrew/bin/podman" : "/usr/bin/podman",
        OperatingSystem.IsMacOS() ? "nightshift-dev" : "local-rootless",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), OperatingSystem.IsMacOS() ? "Library/Application Support/Nightshift/container-workspaces" : ".local/share/nightshift/container-workspaces"), OfficialImage);
}

public sealed class PodmanLauncher(ContainerProfile profile, WorkspaceSnapshot initial, string? ownerDirectory = null) : IWorkerLauncher
{
    public WorkspaceSnapshot Snapshot { get; private set; } = initial;
    public string? LastExecutionId { get; private set; }
    private bool running;
    private IEnumerable<string> Prefix => profile.Connection == "local-rootless" ? ["--remote=false"] : ["--connection", profile.Connection];
    private async Task<HostProcess.Result> Call(IEnumerable<string> args, string input, CancellationToken token, int maxBytes = 32 * 1024 * 1024)
        => await HostProcess.Run(profile.Executable, Prefix.Concat(args), input, token, maxBytes,
            ownerDirectory != null && args.FirstOrDefault() == "start" ? Path.Combine(ownerDirectory, "execution-units", args.Last()) : null);
    private static void Success(HostProcess.Result result)
    { if (result.ExitCode != 0) throw new IOException("Local isolation runtime unavailable or operation failed (exit " + result.ExitCode + "). Start the approved runtime manually; no native fallback."); }
    public async Task<RuntimeIdentity> VerifyAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) throw new IOException("Protected profile requires Linux containers; Windows host profile unverified");
        if (!Path.IsPathFullyQualified(profile.Executable) || !File.Exists(profile.Executable)) throw new IOException("Approved Podman executable unavailable");
        if (!Regex.IsMatch(profile.Image, @"^[a-z0-9./:_-]+@sha256:[a-f0-9]{64}$")) throw new IOException("Profile requires an immutable image digest");
        if (Path.GetFullPath(profile.ShareRoot) != Path.GetFullPath(ContainerProfile.LocalDefault.ShareRoot)) throw new IOException("The initial profile requires the dedicated local workspace share");
        WorkspaceSnapshot.RejectLinks(profile.ShareRoot);
        if (!Directory.Exists(profile.ShareRoot)) throw new IOException("Approved dedicated workspace share is missing; operator setup required");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        if (profile.Connection != "local-rootless")
        {
            if (!OperatingSystem.IsMacOS() || profile.Connection != "nightshift-dev") throw new IOException("Only approved local machine connection is supported");
            var connections = await HostProcess.Run(profile.Executable, ["system", "connection", "list", "--format", "json"], "", deadline.Token); Success(connections);
            using var doc = JsonDocument.Parse(connections.Output);
            var selected = doc.RootElement.EnumerateArray().SingleOrDefault(e => e.GetProperty("Name").GetString() == profile.Connection);
            if (selected.ValueKind == JsonValueKind.Undefined || !selected.GetProperty("IsMachine").GetBoolean()) throw new IOException("Connection is not a local Podman machine");
            var uri = new Uri(selected.GetProperty("URI").GetString()!);
            if (uri.Scheme != "ssh" || uri.Host != "127.0.0.1" || uri.UserInfo != "core" || uri.AbsolutePath != "/run/user/501/podman/podman.sock") throw new IOException("Rootless local connection identity mismatch");
            var machine = await HostProcess.Run(profile.Executable, ["machine", "inspect", "nightshift-dev"], "", deadline.Token); Success(machine);
            using var m = JsonDocument.Parse(machine.Output); var state = m.RootElement[0];
            if (state.GetProperty("State").GetString() != "running" || state.GetProperty("Rootful").GetBoolean()
                || state.GetProperty("SSHConfig").GetProperty("Port").GetInt32() != uri.Port
                || state.GetProperty("SSHConfig").GetProperty("IdentityPath").GetString() != selected.GetProperty("Identity").GetString()) throw new IOException("Approved machine is stopped or its identity changed");
        }
        else if (!OperatingSystem.IsLinux()) throw new IOException("local-rootless requires a Linux host");
        var info = await Call(["info", "--format", "json"], "", deadline.Token); Success(info);
        using var data = JsonDocument.Parse(info.Output); var host = data.RootElement.GetProperty("host");
        if (host.GetProperty("os").GetString() != "linux" || !host.GetProperty("security").GetProperty("rootless").GetBoolean()
            || !host.GetProperty("security").GetProperty("seccompEnabled").GetBoolean()) throw new IOException("Runtime must be rootless Linux with seccomp");
        var image = await Call(["image", "inspect", profile.Image], "", deadline.Token); Success(image);
        using var imageData = JsonDocument.Parse(image.Output);
        if (!imageData.RootElement[0].GetProperty("RepoDigests").EnumerateArray().Any(d => d.GetString() == profile.Image)) throw new IOException("Image digest unavailable; no implicit pull");
        return new("linux-container-v1", data.RootElement.GetProperty("version").GetProperty("Version").GetString()!, profile.Connection, profile.Image,
            host.GetProperty("os").GetString() + "/" + host.GetProperty("arch").GetString());
    }
    private sealed record OwnedUnit(string Id, ContainerProfile Profile, string State);
    private Task Record(string id, string unitState) => ownerDirectory == null ? Task.CompletedTask
        : AtomicFile.WriteJsonAsync(Path.Combine(ownerDirectory, "execution-units", id + ".json"), new OwnedUnit(id, profile, unitState));
    public async Task RecoverAsync(CancellationToken token)
    {
        if (ownerDirectory == null || !Directory.Exists(Path.Combine(ownerDirectory, "execution-units"))) return;
        await VerifyAsync(token);
        foreach (string path in Directory.GetFiles(Path.Combine(ownerDirectory, "execution-units"), "*.json"))
        {
            var unit = Json.Read<OwnedUnit>(await File.ReadAllTextAsync(path, token));
            if (unit.State is "collected" or "interrupted") continue;
            if (!Regex.IsMatch(unit.Id, "^nightshift-[a-f0-9]{32}$") || unit.Profile != profile) throw new IOException("Owned execution recovery identity mismatch");
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Success(await Call(["rm", "--force", "--time=0", "--ignore", unit.Id], "", cleanup.Token));
            await Record(unit.Id, "interrupted");
            string workspace = Path.Combine(profile.ShareRoot, unit.Id);
            if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        }
    }
    public async Task<WorkerResult> ExecuteAsync(WorkerCommand command, CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) throw new IOException("Protected execution requires Linux containers; native Windows is unsupported");
        if (UnixIdentity.UserId == 0) throw new IOException("The protected coordinator must run as a non-root user");
        if (running) throw new InvalidOperationException("One owned execution at a time");
        running = true;
        string id = "nightshift-" + Guid.NewGuid().ToString("N"); LastExecutionId = id;
        string root = Path.Combine(profile.ShareRoot, id), workspace = Path.Combine(root, "workspace");
        bool created = false, removed = false;
        try
        {
            await VerifyAsync(token); token.ThrowIfCancellationRequested();
            if (!command.Executable.StartsWith('/') || command.Executable.Contains('\0')) throw new IOException("Worker executable must be an approved absolute image path");
            Directory.CreateDirectory(root); WorkspaceSnapshot.PrivateDirectory(root); Snapshot.WriteFresh(workspace);
            var args = new List<string> { "create", "--name", id, "--pull=never", "--network=none", "--read-only", "--cap-drop=ALL", "--security-opt=no-new-privileges",
                "--userns=keep-id", "--user", OperatingSystem.IsMacOS() ? "501:501" : UnixIdentity.UserId + ":" + UnixIdentity.GroupId,
                "--pids-limit=64", "--memory=256m", "--cpus=1", "--ulimit", "nofile=256:256", "--ipc=private", "--pid=private",
                "--tmpfs", "/tmp:rw,nosuid,nodev,size=64m,mode=1777", "--workdir", "/workspace", "--env", "HOME=/tmp/home", "--env", "PATH=/usr/bin:/bin",
                "--mount", $"type=bind,src={workspace},dst=/workspace,rw", "--interactive", profile.Image, command.Executable };
            args.AddRange(command.Arguments);
            // Mark ownership before create: interruption after daemon create is cleaned up by name.
            await Record(id, "allocated");
            created = true; var create = await Call(args, "", token); Success(create);
            await Record(id, "running");
            var result = await Call(["start", "--attach", "--interactive", id], command.Input, token, 2 * 1024 * 1024);
            // Attached client exit is not the workload exit code; inspect the identified container.
            var inspect = await Call(["inspect", id], "", token); Success(inspect);
            using var state = JsonDocument.Parse(inspect.Output);
            var s = state.RootElement[0].GetProperty("State");
            if (s.GetProperty("Running").GetBoolean()) throw new IOException("Worker container still running after attached process ended");
            int exit = s.GetProperty("ExitCode").GetInt32();
            // Stop all writers before archive collection, then remove execution before parsing bytes.
            var archive = await Call(["cp", id + ":/workspace/.", "-"], "", token); Success(archive);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Success(await Call(["rm", "--force", "--time=0", id], "", cleanup.Token)); removed = true;
            Snapshot = WorkspaceSnapshot.ReadArchive(archive.Output);
            await Record(id, "collected");
            return new(exit, result.Text, result.Error, id);
        }
        finally
        {
            if (created && !removed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await Call(["rm", "--force", "--time=0", "--ignore", id], "", cleanup.Token);
                Success(result); await Record(id, "interrupted"); // Cleanup uncertainty is an error, never accepted output.
            }
            // No code follows worker links for cleanup; recursive delete unlinks symlinks.
            if (Directory.Exists(root)) Directory.Delete(root, true);
            running = false;
        }
    }
}

internal static class UnixIdentity
{
    [System.Runtime.InteropServices.DllImport("libc")] private static extern uint getuid();
    [System.Runtime.InteropServices.DllImport("libc")] private static extern uint getgid();
    public static uint UserId => getuid();
    public static uint GroupId => getgid();
}
