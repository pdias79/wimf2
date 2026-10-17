namespace Wimf.Catalog;

/// <summary>Summary comparing the previous catalog with a refreshed filesystem scan.</summary>
public sealed record CatalogUpdateSummary(int Total, int Added, int Modified, int Removed, int Unchanged, int Skipped);

/// <summary>
/// Rebuilds the legacy catalog and reports changes relative to its previous snapshot.
/// The filesystem is still fully enumerated; no platform-specific journal is required.
/// </summary>
public static class LegacyCatalogUpdater
{
    public static CatalogUpdateSummary Update(string rootPath, string outputPath,
        string? catalogName = null, CancellationToken cancellationToken = default,
        Action<string>? onSkipped = null, IEnumerable<string>? excludePaths = null,
        Action<int>? onProgress = null, Action<CatalogScanMetrics>? onMetrics = null,
        int directoryPrefetch = 1, Action<int>? onDirectoriesEnumerated = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var root = Path.GetFullPath(rootPath);
        var output = Path.GetFullPath(outputPath);
        if (!File.Exists(output))
            throw new FileNotFoundException("Existing catalog not found; use --build-root to create it first.", output);

        var previous = LegacyCatalogReader.ReadFile(output);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(previous.Header.RootPath)),
                Path.TrimEndingDirectorySeparator(root), pathComparison))
            throw new InvalidDataException("Existing catalog root does not match --build-root.");
        if (previous.Diagnostics.Count > 0)
            throw new InvalidDataException("Existing catalog has invalid entries. Create a new catalog first.");

        var before = MakeIndex(previous);
        var skipped = 0;
        // The writer commits the replacement only after a successful scan.
        LegacyCatalogWriter.Build(root, output, catalogName ?? previous.Header.Name,
            cancellationToken, message => { skipped++; onSkipped?.Invoke(message); }, excludePaths,
            onProgress, onMetrics, directoryPrefetch, onDirectoriesEnumerated);
        var updated = LegacyCatalogReader.ReadFile(output);
        // Compare against one mutable index instead of retaining a second full
        // dictionary of paths. Entries left in "before" were removed.
        var added = 0;
        var modified = 0;
        var unchanged = 0;
        var total = 0;
        foreach (var entry in updated.EnumerateEntries())
        {
            total++;
            var path = updated.GetFullPath(entry.Id);
            var state = (entry.Extension, entry.Size, entry.ModifiedAt.ToUnixTimeSeconds() / 86400);
            if (before.Remove(path, out var oldState))
            {
                if (state == oldState) unchanged++;
                else modified++;
            }
            else added++;
        }
        return new CatalogUpdateSummary(total, added, modified, before.Count, unchanged, skipped);
    }

    private static Dictionary<string, (string Extension, ulong Size, long Day)> MakeIndex(Catalog catalog)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var index = new Dictionary<string, (string, ulong, long)>(comparer);
        foreach (var entry in catalog.EnumerateEntries())
        {
            // Persisted paths and metadata are compared without opening file contents.
            index.Add(catalog.GetFullPath(entry.Id),
                (entry.Extension, entry.Size, entry.ModifiedAt.ToUnixTimeSeconds() / 86400));
        }
        return index;
    }
}
