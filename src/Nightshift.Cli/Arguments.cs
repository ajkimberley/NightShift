using System.Globalization;

namespace Nightshift.Cli;

public sealed class Arguments
{
    private static readonly HashSet<string> Switches = ["--enable-codex", "--latest", "--json", "--help", "--write", "--enable-agent"];
    private static readonly HashSet<string> Values = ["--dir", "--answers", "--backend", "--codex", "--sandbox", "--max-visits", "--max-seconds", "--pause-after", "--workdir", "--project", "--config", "--workflow", "--output-root", "--output", "--probe-seconds", "--profile", "--state-root", "--policy", "--agent-executable", "--repository-service", "--pipeline-service"];
    public string Command { get; }
    public string? Target { get; private set; }
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private readonly HashSet<string> switches = new(StringComparer.Ordinal);
    public List<string> Outputs { get; } = [];
    public Arguments(string[] args)
    {
        Command = args.Length == 0 ? "help" : args[0] switch { "--help" or "-h" => "help", "--version" => "version", var c => c };
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (Switches.Contains(arg)) { if (!switches.Add(arg)) throw new ArgumentException($"Duplicate option {arg}"); continue; }
            if (Values.Contains(arg))
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Missing value for {arg}");
                if (arg == "--output") { Outputs.Add(args[i]); continue; }
                if (!values.TryAdd(arg, args[i])) throw new ArgumentException($"Duplicate option {arg}");
                continue;
            }
            if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option '{arg}'. Run nightshift help.");
            if (Target != null) throw new ArgumentException($"Unexpected argument '{arg}'");
            Target = arg;
        }
    }
    public string? Get(string key) => values.GetValueOrDefault(key);
    public bool Has(string key) => values.ContainsKey(key) || switches.Contains(key);
    public int Integer(string key, int fallback) => Get(key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
    public double Number(string key, double fallback) => Get(key) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
