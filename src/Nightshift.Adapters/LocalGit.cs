using Nightshift.Core;

namespace Nightshift.Adapters;

// Git administration is coordinator-private and freshly initialized. Worker .git is never adopted.
public sealed class LocalGit(string executable = "/usr/bin/git")
{
    private async Task<string> Git(string root, CancellationToken token, params string[] args)
    {
        var result = await HostProcess.Run(executable, new[] { "--no-optional-locks", "-c", "core.hooksPath=/dev/null", "-c", "core.fsmonitor=false",
            "-c", "init.templateDir=", "-c", "user.name=Nightshift fixture", "-c", "user.email=nightshift@invalid", "-C", root }.Concat(args), "", token);
        if (result.ExitCode != 0) throw new IOException("Coordinator local Git operation failed (" + result.ExitCode + ")");
        return result.Text.Trim();
    }
    public async Task<Candidate> CommitSnapshotAsync(string privateDirectory, WorkspaceSnapshot snapshot, RepositoryIdentity repository, CancellationToken token)
    {
        snapshot.WriteFresh(privateDirectory);
        await Git(privateDirectory, token, "init", "-q");
        // Plumbing bypasses .gitignore, .gitattributes filters and line-ending conversion.
        foreach (var file in snapshot.Files)
        {
            string blob = await Git(privateDirectory, token, "hash-object", "-w", "--no-filters", "--", file.Path);
            await Git(privateDirectory, token, "update-index", "--add", "--cacheinfo", file.Executable ? "100755" : "100644", blob, file.Path);
        }
        await Git(privateDirectory, token, "commit", "--no-verify", "--allow-empty", "-q", "-m", "Captured Nightshift candidate");
        string revision = await Git(privateDirectory, token, "rev-parse", "--verify", "HEAD^{commit}");
        return new(repository, revision, snapshot.Digest);
    }
    public async Task<string> ResolveRevisionAsync(string directory, string revision, CancellationToken token)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(revision, "^[a-fA-F0-9]{40,64}$")) throw new ArgumentException("An exact immutable Git object ID is required");
        return await Git(directory, token, "rev-parse", "--verify", revision + "^{commit}");
    }
}
