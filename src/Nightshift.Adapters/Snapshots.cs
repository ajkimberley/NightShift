using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;

namespace Nightshift.Adapters;

public sealed record SnapshotFile(string Path, byte[] Bytes, bool Executable);
public sealed class WorkspaceSnapshot
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public const int MaximumFiles = 4096;
    private readonly SnapshotFile[] files;
    public IReadOnlyList<SnapshotFile> Files => files.Select(f => f with { Bytes = f.Bytes.ToArray() }).ToArray();
    public WorkspaceSnapshot(IEnumerable<SnapshotFile> source)
    {
        files = source.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => f with { Bytes = f.Bytes.ToArray() }).ToArray();
        if (files.Length > MaximumFiles || files.Sum(f => (long)f.Bytes.Length) > MaximumBytes) throw new IOException("Snapshot exceeds collection limits");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) { ValidateName(f.Path); if (!names.Add(f.Path)) throw new IOException("Duplicate/ambiguous artifact path"); }
    }
    public string Digest
    {
        get
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var f in files)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(f.Path + "\0" + (f.Executable ? "x" : "-") + "\0" + f.Bytes.Length + "\0")); hash.AppendData(f.Bytes);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }
    public static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 1024 || name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Contains('\0')
            || name.Split('/').Any(p => p is "" or "." or ".." || p.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Unsafe artifact path");
    }
    public static WorkspaceSnapshot Capture(string root)
    {
        root = Path.GetFullPath(root); RejectLinks(root);
        var list = new List<SnapshotFile>(); long total = 0;
        void Walk(string dir)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(dir).Order(StringComparer.Ordinal))
            {
                if (new[] { ".git", ".nightshift", "bin", "obj", "runs", "artifacts", ".test-runs" }.Contains(Path.GetFileName(path))) continue;
                RejectLinks(path);
                if (Directory.Exists(path)) { Walk(path); continue; }
                var info = new FileInfo(path);
                if (info.Length > MaximumBytes || (total += info.Length) > MaximumBytes || list.Count >= MaximumFiles) throw new IOException("Snapshot exceeds collection limits");
                list.Add(new(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes(path), !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0));
            }
        }
        Walk(root); return new(list);
    }
    public static void RejectLinks(string path)
    {
        for (var dir = new FileInfo(Path.GetFullPath(path)); dir != null; dir = dir.Directory is { } parent ? new FileInfo(parent.FullName) : null)
            if (dir.LinkTarget != null || ((int)dir.Attributes != -1 && (dir.Attributes & FileAttributes.ReparsePoint) != 0)) throw new IOException("Symlink paths are not allowed: " + path);
    }
    public void WriteFresh(string root)
    {
        if (Directory.Exists(root)) throw new IOException("Snapshot destination must be fresh");
        RejectLinks(Path.GetDirectoryName(Path.GetFullPath(root))!);
        Directory.CreateDirectory(root); PrivateDirectory(root);
        foreach (var f in files)
        {
            string path = Path.Combine(root, f.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(f.Bytes);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | (f.Executable ? UnixFileMode.UserExecute : 0));
        }
    }
    public static void PrivateDirectory(string root)
    { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    public static WorkspaceSnapshot ReadArchive(byte[] archive)
    {
        var list = new List<SnapshotFile>(); long total = 0;
        using var reader = new TarReader(new MemoryStream(archive)); TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            string name = entry.Name;
            while (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            if (name is "" or ".") { if (entry.EntryType != TarEntryType.Directory) throw new IOException("Invalid root archive entry"); continue; }
            if (entry.EntryType == TarEntryType.Directory) { ValidateName(name.TrimEnd('/')); continue; }
            ValidateName(name);
            if (entry.EntryType is not TarEntryType.RegularFile and not TarEntryType.V7RegularFile) throw new IOException("Artifact links and special files are refused");
            if (entry.Length > MaximumBytes || (total += entry.Length) > MaximumBytes || list.Count >= MaximumFiles) throw new IOException("Artifact bounds exceeded");
            using var bytes = new MemoryStream(); entry.DataStream?.CopyTo(bytes);
            list.Add(new(name, bytes.ToArray(), (entry.Mode & UnixFileMode.UserExecute) != 0));
        }
        return new(list);
    }
}
