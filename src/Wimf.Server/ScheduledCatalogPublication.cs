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
        var skipped = 0;
        try
        {
            var count = LegacyCatalogWriter.Build(root, staging, name, cancellationToken,
                message => { skipped++; onSkipped?.Invoke(message); }, exclusions, onProgress,
                onMetrics, directoryPrefetch, onDirectoriesEnumerated);
            cancellationToken.ThrowIfCancellationRequested();
            if (skipped != 0)
                throw new InvalidDataException($"Full reconciliation skipped {skipped} entries; existing catalog preserved.");
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
