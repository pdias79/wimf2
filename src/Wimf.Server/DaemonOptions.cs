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
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DaemonConfiguration))]
internal partial class DaemonConfigurationJsonContext : JsonSerializerContext;

public sealed record DaemonOptions(
    string[] Catalogs, string[] Sources, string? CacheDirectory,
    int RefreshSeconds, bool EnableFileActions)
{
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
        if (configurationFile is not null)
        {
            var fullPath = Path.GetFullPath(configurationFile);
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
            if (document.Catalogs is null || document.Sources is null)
                throw new ArgumentException("catalogs and sources must be arrays.");
            catalogs.AddRange(document.Catalogs.Select(path => Resolve(baseDirectory, path)));
            sources.AddRange(document.Sources.Select(path => Resolve(baseDirectory, path)));
            cache = document.CacheDirectory is null ? null : Resolve(baseDirectory, document.CacheDirectory);
            refresh = document.RefreshSeconds;
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
        return new DaemonOptions(catalogs.ToArray(), sources.ToArray(), cache, refresh, fileActions);
    }
}
