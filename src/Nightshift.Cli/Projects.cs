using System.Text.Json;
using System.Text.Json.Serialization;
using Nightshift.Core;

namespace Nightshift.Cli;

public sealed record ProjectConfiguration
{
    public int SchemaVersion { get; init; } = 1;
    public string WorkingDirectory { get; init; } = ".";
    public string DefaultWorkflow { get; init; } = "workflow.dot";
    public string Backend { get; init; } = "scripted";
    public string? CodexExecutable { get; init; }
    public string Sandbox { get; init; } = "read-only";
    public bool EnableCodex { get; init; }
    public string RunOutputRoot { get; init; } = ".nightshift/runs";
    public int MaxVisits { get; init; } = 1000;
    public double MaxSeconds { get; init; } = 3600;
    public List<string> Outputs { get; init; } = [];
    public void Validate()
    {
        if (SchemaVersion != 1) throw new ArgumentException($"Unsupported nightshift.json schema_version {SchemaVersion}; expected 1.");
        if (Backend is not "codex" and not "scripted") throw new ArgumentException("backend must be codex or scripted.");
        if (Sandbox is not "read-only" and not "workspace-write") throw new ArgumentException("sandbox must be read-only or workspace-write.");
        if (MaxVisits <= 0 || MaxSeconds <= 0 || !double.IsFinite(MaxSeconds)) throw new ArgumentException("Run limits must be positive and finite.");
        if (new[] { WorkingDirectory, DefaultWorkflow, RunOutputRoot }.Any(string.IsNullOrWhiteSpace) || Outputs == null || Outputs.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Configuration paths must be nonempty.");
        if (CodexExecutable != null && string.IsNullOrWhiteSpace(CodexExecutable)) throw new ArgumentException("codex_executable must be a path or null for discovery.");
    }
}
public sealed record Project(string Root, string? ConfigurationPath, ProjectConfiguration Configuration)
{
    public static string FullPath(string value, string origin) => Path.GetFullPath(value, origin);
    public string Resolve(string value) => FullPath(value, Root);
    public static Project Discover(string invocation, string? explicitProject = null, string? explicitConfig = null)
    {
        string start = explicitProject == null ? Path.GetFullPath(invocation) : FullPath(explicitProject, invocation);
        if (!Directory.Exists(start)) throw new DirectoryNotFoundException($"Project directory does not exist: {start}");
        if (explicitConfig != null) return Load(FullPath(explicitConfig, invocation));
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (var current = new DirectoryInfo(start); current != null; current = current.Parent)
        {
            if (current.FullName == home && current.FullName != start) break;
            string candidate = Path.Combine(current.FullName, "nightshift.json");
            if (File.Exists(candidate)) return Load(candidate);
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git"))) break;
        }
        return new(start, null, new());
    }
    public static Project Load(string path)
    {
        try
        {
            var opts = new JsonSerializerOptions(Json.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            var config = JsonSerializer.Deserialize<ProjectConfiguration>(File.ReadAllText(path), opts) ?? throw new JsonException("Null configuration");
            config.Validate(); return new(Path.GetDirectoryName(path)!, path, config);
        }
        catch (JsonException e) { throw new ArgumentException($"Invalid configuration {path}: {e.Message}"); }
    }
    public static string Asset(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", file);
        if (!File.Exists(path)) throw new FileNotFoundException($"Packaged asset is missing: {path}. Reinstall Nightshift.");
        return path;
    }
    public static async Task<Project> InitializeAsync(string invocation, Arguments args, CancellationToken token)
    {
        string root = args.Get("--project") is { } p ? FullPath(p, invocation) : invocation;
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Create your project directory first: {root}");
        string configFile = Path.Combine(root, "nightshift.json");
        if (File.Exists(configFile)) throw new ArgumentException($"Configuration already exists: {configFile}. Edit it explicitly; init will not overwrite it.");
        string workflow = args.Get("--workflow") ?? "workflow.dot";
        var config = new ProjectConfiguration
        {
            Backend = args.Get("--backend") ?? "scripted", EnableCodex = args.Has("--enable-codex"),
            CodexExecutable = args.Get("--codex") is { } exe ? FullPath(exe, invocation) : null,
            WorkingDirectory = args.Get("--workdir") is { } wd ? FullPath(wd, invocation) : ".",
            DefaultWorkflow = args.Get("--workflow") is { } wf ? FullPath(wf, invocation) : workflow,
            Sandbox = args.Get("--sandbox") ?? "read-only", RunOutputRoot = args.Get("--output-root") is { } output ? FullPath(output, invocation) : ".nightshift/runs",
            MaxVisits = args.Integer("--max-visits", 1000), MaxSeconds = args.Number("--max-seconds", 3600),
            Outputs = args.Outputs.Select(o => FullPath(o, invocation)).ToList()
        };
        config.Validate();
        if (config.Backend == "codex" && !config.EnableCodex) throw new ArgumentException("Choosing Codex requires init --backend codex --enable-codex to save explicit real-worker permission. No worker has started.");
        if (config.CodexExecutable != null) CodexDiscovery.RequireExecutable(config.CodexExecutable);
        var project = new Project(root, configFile, config);
        if (!Directory.Exists(project.Resolve(config.WorkingDirectory))) throw new DirectoryNotFoundException("Configured working_directory does not exist.");
        string workflowPath = project.Resolve(config.DefaultWorkflow);
        // Existing user workflows are retained verbatim. No force/overwrite mode is implicit.
        if (!File.Exists(workflowPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)!);
            using var stream = new FileStream(workflowPath, FileMode.CreateNew, FileAccess.Write);
            await using var writer = new StreamWriter(stream); await writer.WriteAsync((await File.ReadAllTextAsync(Asset("workflow.dot"), token)).AsMemory(), token);
        }
        await AtomicFile.WriteJsonAsync(configFile, config, token);
        string relativeOutput = Path.GetRelativePath(root, project.Resolve(config.RunOutputRoot));
        if (relativeOutput != "." && !relativeOutput.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativeOutput))
        {
            string entry = "/" + relativeOutput.Replace(Path.DirectorySeparatorChar, '/') + "/";
            string ignoreFile = Path.Combine(root, ".gitignore"); string original = File.Exists(ignoreFile) ? await File.ReadAllTextAsync(ignoreFile, token) : "";
            if (!original.Split('\n').Any(line => line.TrimEnd('\r') == entry)) await File.AppendAllTextAsync(ignoreFile, (original.Length > 0 && !original.EndsWith('\n') ? "\n" : "") + entry + "\n", token);
        }
        return project;
    }
}
