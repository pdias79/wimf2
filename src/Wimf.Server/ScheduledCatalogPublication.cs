using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>
/// Safe scheduled full reconciliation. Build and validate off to the side,
/// then replace the catalog only after a successful, complete scan.
/// Incremental SMB notification hints are deliberately not trusted here.
/// </summary>
public static class ScheduledCatalogPublication
{
    public static int Refresh(string root, string output, string? name = null,
        CancellationToken cancellationToken = default,
        Action<string>? onSkipped = null, IEnumerable<string>? exclusions = null,
        Action<int>? onProgress = null, Action<CatalogScanMetrics>? onMetrics = null,
        int directoryPrefetch = 1, Action<int>? onDirectoriesEnumerated = null)
    {
        var target = Path.GetFullPath(output);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var suffix = target.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? ".txt.gz" : ".txt";
        var staging = Path.Combine(directory, ".wimf-staging-" + Guid.NewGuid().ToString("N") + suffix);
        var inaccessiblePaths = new List<string>();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            var count = LegacyCatalogWriter.Build(root, staging, name, cancellationToken,
                message =>
                {
                    if (message.StartsWith("Skipped inaccessible ", StringComparison.Ordinal))
                    {
                        var colon = message.IndexOf(": ", StringComparison.Ordinal);
                        var detail = colon >= 0 ? message[(colon + 2)..] : "";
                        var reason = detail.LastIndexOf(" (", StringComparison.Ordinal);
                        if (reason >= 0) detail = detail[..reason];
                        inaccessiblePaths.Add(detail);
                    }
                    onSkipped?.Invoke(message);
                }, exclusions, onProgress,
                onMetrics, directoryPrefetch, onDirectoriesEnumerated);
            cancellationToken.ThrowIfCancellationRequested();
            if (inaccessiblePaths.Count != 0)
            {
                // A previously indexed file or subtree must never disappear only
                // because access was denied during this scan.
                if (!File.Exists(target))
                    throw new InvalidDataException("Initial scan encountered inaccessible entries; catalog not published.");
                var old = LegacyCatalogReader.ReadFile(target);
                if (old.Diagnostics.Count != 0 ||
                    !Path.TrimEndingDirectorySeparator(Path.GetFullPath(old.Header.RootPath))
                        .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), comparison))
                    throw new InvalidDataException("Previous catalog cannot validate inaccessible paths.");
                var priorPaths = old.EnumerateEntries()
                    .Select(entry => old.GetFullPath(entry.Id)).ToArray();
                if (inaccessiblePaths.Any(path => string.IsNullOrWhiteSpace(path) ||
                    priorPaths.Any(previous => previous.Equals(path, comparison) ||
                        previous.StartsWith(path.TrimEnd(Path.DirectorySeparatorChar) +
                            Path.DirectorySeparatorChar, comparison))))
                    throw new InvalidDataException("Previously indexed entries became inaccessible; existing catalog preserved.");
            }
            var catalog = LegacyCatalogReader.ReadFile(staging);
            if (catalog.Diagnostics.Count != 0 || catalog.EnumerateEntries().Count() != count)
                throw new InvalidDataException("Staged catalog validation failed; existing catalog preserved.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, target, overwrite: true);
            return count;
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }
}
