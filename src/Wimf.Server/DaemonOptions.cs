using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wimf.Server;

/// <summary>Persistent startup settings. File actions deliberately remain a CLI-only opt-in.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DaemonConfiguration
{
    public string[] Catalogs { get; init; } = [];
    public string[] Sources { get; init; } = [];
    public string? CacheDirectory { get; init; }
    public int RefreshSeconds { get; init; } = 300;
    public IndexingJobConfiguration[] Indexing { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class IndexingJobConfiguration
{
    public string Root { get; init; } = "";
    public string Output { get; init; } = "";
    public string? Name { get; init; }
    public string[] Exclude { get; init; } = [];
    public int? IntervalMinutes { get; init; }
    public int? IntervalHours { get; init; }
    public int? IntervalDays { get; init; }
}

public sealed record IndexingJob(string Root, string Output, string? Name,
    string[] Exclude, TimeSpan Interval);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DaemonConfiguration))]
internal partial class DaemonConfigurationJsonContext : JsonSerializerContext;

public sealed record DaemonOptions(
    string[] Catalogs, string[] Sources, string? CacheDirectory,
    int RefreshSeconds, bool EnableFileActions, IndexingJob[]? Indexing = null)
{
    /// <summary>Find a relative config in the daemon's working directory or its ancestors.</summary>
    public static string ResolveConfigurationFilePath(string path, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (Path.IsPathFullyQualified(path))
            return Path.GetFullPath(path);

        var directory = new DirectoryInfo(Path.GetFullPath(workingDirectory));
        var original = Path.GetFullPath(path, directory.FullName);
        for (var current = directory; current is not null; current = current.Parent)
        {
            var candidate = Path.GetFullPath(path, current.FullName);
            if (File.Exists(candidate)) return candidate;
        }
        // Keep the original candidate in the exception when no file is found.
        return original;
    }

    public static DaemonOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? configurationFile = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--config") continue;
            if (configurationFile is not null || i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("--config requires exactly one file path.");
            configurationFile = args[++i];
        }

        var catalogs = new List<string>();
        var sources = new List<string>();
        string? cache = null;
        var refresh = 300;
        var indexing = new List<IndexingJob>();
        if (configurationFile is not null)
        {
            var fullPath = ResolveConfigurationFilePath(configurationFile, Environment.CurrentDirectory);
            var document = JsonSerializer.Deserialize(
                File.ReadAllText(fullPath), DaemonConfigurationJsonContext.Default.DaemonConfiguration)
                ?? throw new InvalidDataException("The configuration must be a JSON object.");
            if (document.RefreshSeconds < 0)
                throw new ArgumentException("refreshSeconds must be nonnegative.");
            var baseDirectory = Path.GetDirectoryName(fullPath)!;
            static string Resolve(string baseDirectory, string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("Configuration paths cannot be empty.");
                return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
            }
            catalogs.AddRange((document.Catalogs ?? []).Select(path => Resolve(baseDirectory, path)));
            sources.AddRange((document.Sources ?? []).Select(path => Resolve(baseDirectory, path)));
            cache = document.CacheDirectory is null ? null : Resolve(baseDirectory, document.CacheDirectory);
            refresh = document.RefreshSeconds;
            foreach (var job in document.Indexing ?? [])
            {
                if (job is null) throw new ArgumentException("indexing entries must be objects.");
                var specifiedIntervals = (job.IntervalMinutes.HasValue ? 1 : 0) +
                    (job.IntervalHours.HasValue ? 1 : 0) + (job.IntervalDays.HasValue ? 1 : 0);
                if (specifiedIntervals != 1)
                    throw new ArgumentException("Specify exactly one of intervalMinutes, intervalHours or intervalDays for each indexing job.");
                var minutes = job.IntervalMinutes.HasValue ? (long)job.IntervalMinutes.Value :
                    job.IntervalHours.HasValue ? (long)job.IntervalHours.Value * 60 :
                    (long)job.IntervalDays!.Value * 24 * 60;
                if (minutes < 1 || minutes > 3650L * 24 * 60)
                    throw new ArgumentException("Indexing intervals must be between one minute and ten years.");
                var root = Resolve(baseDirectory, job.Root);
                var output = Resolve(baseDirectory, job.Output);
                if (job.Exclude is null) throw new ArgumentException("indexing.exclude must be an array.");
                _ = new Wimf.Catalog.CatalogPathExclusions(root, job.Exclude);
                indexing.Add(new IndexingJob(root, output, job.Name, job.Exclude,
                    TimeSpan.FromMinutes(minutes)));
            }
        }

        var fileActions = false;
        var cliCatalogs = new List<string>();
        var cliSources = new List<string>();
        string? cliCache = null;
        int? cliRefresh = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" when i + 1 < args.Length:
                    i++;
                    break;
                case "--enable-file-actions":
                    fileActions = true;
                    break;
                case "--catalog" when i + 1 < args.Length:
                    cliCatalogs.Add(args[++i]);
                    break;
                case "--source" when i + 1 < args.Length:
                    cliSources.Add(args[++i]);
                    break;
                case "--cache-dir" when i + 1 < args.Length:
                    cliCache = args[++i];
                    break;
                case "--refresh-seconds" when i + 1 < args.Length &&
                    int.TryParse(args[i + 1], out var seconds) && seconds >= 0:
                    cliRefresh = seconds;
                    i++;
                    break;
                default:
                    throw new ArgumentException("Unknown or invalid argument: " + args[i]);
            }
        }

        // Explicit CLI catalogs/sources replace their corresponding configured lists.
        if (cliCatalogs.Count > 0) catalogs = cliCatalogs;
        if (cliSources.Count > 0) sources = cliSources;
        if (cliCache is not null) cache = cliCache;
        if (cliRefresh is not null) refresh = cliRefresh.Value;

        if (catalogs.Any(string.IsNullOrWhiteSpace) || sources.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Catalog and source paths cannot be empty.");
        if (sources.Count > 0 && string.IsNullOrWhiteSpace(cache))
            throw new ArgumentException("--cache-dir or cacheDirectory is required when sources are provided.");
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var localPaths = new HashSet<string>(catalogs.Select(Path.GetFullPath), comparer);
        var indexedOutputs = new HashSet<string>(comparer);
        foreach (var job in indexing)
        {
            if (!localPaths.Contains(job.Output))
                throw new ArgumentException("Every indexing output must also appear in catalogs.");
            if (!indexedOutputs.Add(job.Output))
                throw new ArgumentException("Each indexing output must be configured only once.");
        }
        return new DaemonOptions(catalogs.ToArray(), sources.ToArray(), cache, refresh, fileActions, indexing.ToArray());
    }
}
