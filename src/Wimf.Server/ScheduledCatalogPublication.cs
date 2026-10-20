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
        var originalStamp = CatalogFileStamp.Read(target);
        var suffix = target.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? ".txt.gz" : ".txt";
        var staging = Path.Combine(directory, ".wimf-staging-" + Guid.NewGuid().ToString("N") + suffix);
        try
        {
            // Confirm root access before enumeration, not only Directory.Exists.
            EnsureRootAccessible(root);
            var count = LegacyCatalogWriter.Build(root, staging, name, cancellationToken,
                message =>
                {
                    // Detect disconnection at the point of a failed child enumeration,
                    // not only before and after a potentially long SMB scan.
                    if (message.StartsWith("Skipped inaccessible ", StringComparison.Ordinal))
                        EnsureRootAccessible(root);
                    onSkipped?.Invoke(message);
                }, exclusions, onProgress, onMetrics, directoryPrefetch,
                onDirectoriesEnumerated);
            cancellationToken.ThrowIfCancellationRequested();
            // A share may disconnect while children are being enumerated.
            EnsureRootAccessible(root);
            var catalog = LegacyCatalogReader.ReadFile(staging);
            if (catalog.Diagnostics.Count != 0 || catalog.EnumerateEntries().Count() != count)
                throw new InvalidDataException("Staged catalog validation failed; existing catalog preserved.");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRootAccessible(root);
            if (!CatalogFileStamp.Read(target).Equals(originalStamp))
                throw new IOException("Catalog changed during full-scan preparation; publication refused.");
            File.Move(staging, target, overwrite: true);
            return count;
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private static void EnsureRootAccessible(string root)
    {
        try
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
            _ = enumerator.MoveNext();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or DirectoryNotFoundException or System.Security.SecurityException)
        {
            throw new InvalidDataException("Indexing root is not accessible; previous catalog preserved.", ex);
        }
    }
}
