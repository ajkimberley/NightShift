using System.Text;
using Nightshift.Core;

namespace Nightshift.Adapters;

public sealed record ApprovedCheck(string Id, string Command);
public sealed record PinnedFile(string Path, string Base64);
public sealed record ApprovedPolicy(string Workflow, string Backend, string AgentExecutable, ContainerProfile Runtime,
    ApprovedCheck[] Checks, PinnedFile[] PinnedFiles, string RepositoryService = "none", string PipelineService = "none", bool AllowModel = false,
    int MaxVisits = 1000, double MaxSeconds = 3600);
public sealed record ProtectedIdentity(string PolicyRevision, RuntimeIdentity Runtime, AgentCapabilities Agent,
    string ProjectRoot, string CoordinatorVersion, string RepositoryService, string PipelineService);
public sealed class ProtectedState
{
    public string Root { get; }
    public string ProjectRoot { get; }
    public string ProjectDirectory { get; }
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        OperatingSystem.IsMacOS() ? "Library/Application Support/Nightshift/state" : ".local/state/nightshift");
    public ProtectedState(string projectRoot, string? stateRoot = null)
    {
        ProjectRoot = Path.GetFullPath(projectRoot); Root = Path.GetFullPath(stateRoot ?? DefaultRoot);
        WorkspaceSnapshot.RejectLinks(ProjectRoot);
        // State roots come only from explicit operator arguments, never project/agent context.
        if (Within(Root, ProjectRoot) || Within(ProjectRoot, Root) || Within(Root, ContainerProfile.LocalDefault.ShareRoot)
            || Within(ContainerProfile.LocalDefault.ShareRoot, Root)) throw new IOException("Protected state must be separate from project and container workspace sharing");
        WorkspaceSnapshot.RejectLinks(Root); Directory.CreateDirectory(Root); WorkspaceSnapshot.PrivateDirectory(Root);
        ProjectDirectory = Path.Combine(Root, "projects", Engine.Hash(ProjectRoot)[..24]); Directory.CreateDirectory(ProjectDirectory);
        WorkspaceSnapshot.PrivateDirectory(ProjectDirectory);
    }
    public static bool Within(string path, string root) => Path.GetFullPath(path) == Path.GetFullPath(root)
        || Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    public async Task<string> AdoptAsync(ApprovedPolicy policy, CancellationToken token)
    {
        if (policy.Runtime.Connection != ContainerProfile.LocalDefault.Connection || Path.GetFullPath(policy.Runtime.ShareRoot) != Path.GetFullPath(ContainerProfile.LocalDefault.ShareRoot))
            throw new IOException("Only the dedicated local runtime/share is approved for this initial profile");
        if (Within(Root, policy.Runtime.ShareRoot) || Within(ProjectRoot, policy.Runtime.ShareRoot)) throw new IOException("Policy exposes protected paths");
        if (policy.Backend is not "scripted" and not "codex" and not "claude-code" and not "fake-codex" and not "fake-claude") throw new ArgumentException("Unsupported agent backend");
        if (policy.Backend is "codex" or "claude-code" && !policy.AllowModel) throw new ArgumentException("Live backend adoption requires explicit model permission");
        if (policy.Checks.Select(c => c.Id).Distinct().Count() != policy.Checks.Length || policy.Checks.Any(c => string.IsNullOrWhiteSpace(c.Id))) throw new ArgumentException("Check IDs must be unique/nonempty");
        foreach (var f in policy.PinnedFiles) { WorkspaceSnapshot.ValidateName(f.Path); Convert.FromBase64String(f.Base64); }
        string revision = Engine.Hash(Json.Serialize(policy));
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "policies"));
        await AtomicFile.WriteJsonAsync(Path.Combine(ProjectDirectory, "policies", revision + ".json"), policy, token);
        await AtomicFile.WriteJsonAsync(Path.Combine(ProjectDirectory, "approved.json"), revision, token);
        return revision;
    }
    public (string Revision, ApprovedPolicy Policy) Policy(string? revision = null)
    {
        revision ??= File.Exists(Path.Combine(ProjectDirectory, "approved.json"))
            ? Json.Read<string>(File.ReadAllText(Path.Combine(ProjectDirectory, "approved.json"))) : throw new ArgumentException("No approved protected policy. Run nightshift adopt --policy FILE after reviewing it, or explicitly choose --profile native-development.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(revision, "^[A-F0-9]{64}$")) throw new IOException("Invalid protected policy identity");
        var policy = Json.Read<ApprovedPolicy>(File.ReadAllText(Path.Combine(ProjectDirectory, "policies", revision + ".json")));
        if (Engine.Hash(Json.Serialize(policy)) != revision) throw new IOException("Approved policy digest mismatch");
        return (revision, policy);
    }
    public string NewRun() => Path.Combine(ProjectDirectory, "runs", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
    public void RequireRun(string path)
    {
        if (!Within(path, Path.Combine(ProjectDirectory, "runs"))) throw new IOException("Protected run must be in this project's external state store; legacy resume is refused");
        WorkspaceSnapshot.RejectLinks(path);
    }
    public static WorkspaceSnapshot ReadSnapshot(string run)
        => new(Json.Read<SnapshotFile[]>(File.ReadAllText(Path.Combine(run, "candidate.json"))));
    public static Task SaveSnapshot(string run, WorkspaceSnapshot snapshot)
        => AtomicFile.WriteJsonAsync(Path.Combine(run, "candidate.json"), snapshot.Files);
}
