using System.Security.Cryptography;
using System.Text;
using Wimf.Catalog;
using FileCatalog = Wimf.Catalog.Catalog;

namespace Wimf.Server;

/// <summary>
/// Startup-only synchronization of local/UNC catalog sources into an atomic local cache.
/// A failed copy or invalid update never replaces a previously usable cached catalog.
/// </summary>
public static class CatalogCache
{
    public static string CachePath(string sourcePath, string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        var absolute = Path.GetFullPath(sourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(absolute)));
        var suffix = absolute.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? ".gz" : ".txt";
        return Path.Combine(Path.GetFullPath(cacheDirectory), hash + suffix);
    }

    /// <summary>
    /// Uses source metadata (size and UTC write-time) to skip unchanged copies.
    /// On unavailable/invalid source, uses the last valid cache if possible.
    /// The source must be a trusted catalog path, not an arbitrary client request.
    /// </summary>
    public static (FileCatalog Catalog, bool UsedFallback) Load(
        string sourcePath, string cacheDirectory, Action<string>? report = null,
        FileCatalog? previouslyLoaded = null)
    {
        var target = CachePath(sourcePath, cacheDirectory);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var source = new FileInfo(sourcePath);
            source.Refresh();
            if (!source.Exists)
                throw new FileNotFoundException("Catalog source does not exist.", sourcePath);

            var local = new FileInfo(target);
            local.Refresh();
            if (local.Exists &&
                local.Length == source.Length &&
                local.LastWriteTimeUtc == source.LastWriteTimeUtc)
            {
                // Same source and cached-file metadata: the store already owns a parsed instance.
                // Avoid allocating an entire second object graph on every polling cycle.
                if (previouslyLoaded is not null) return (previouslyLoaded, false);
                try { return (LegacyCatalogReader.ReadFile(target), false); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or
                                               UnauthorizedAccessException or ArgumentException)
                {
                    report?.Invoke($"Cached catalog is invalid; refreshing from source: {ex.Message}");
                }
            }

            File.Copy(sourcePath, temporary);
            // Do not publish a partial or malformed catalog.
            FileCatalog parsed;
            using (var validationStream = File.OpenRead(temporary))
                parsed = LegacyCatalogReader.Read(validationStream,
                    compressed: sourcePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
            File.SetLastWriteTimeUtc(temporary, source.LastWriteTimeUtc);
            File.Move(temporary, target, overwrite: true);
            report?.Invoke($"Updated cached catalog from '{sourcePath}'.");
            return (parsed, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or ArgumentException)
        {
            report?.Invoke($"Unable to update source '{sourcePath}': {ex.Message}");
            // A previously validated in-memory catalog is safer and cheaper than
            // rereading the cache on every offline polling cycle.
            if (previouslyLoaded is not null)
            {
                report?.Invoke($"Keeping previously loaded catalog for '{sourcePath}' in memory.");
                return (previouslyLoaded, true);
            }
            // On first startup, cached files still require validation before use.
            try
            {
                var catalog = LegacyCatalogReader.ReadFile(target);
                report?.Invoke($"Using last valid local copy of '{sourcePath}'.");
                return (catalog, true);
            }
            catch (Exception fallbackEx) when (fallbackEx is IOException or UnauthorizedAccessException or
                                               InvalidDataException or ArgumentException)
            {
                throw new IOException($"Source and cached catalog are both unavailable for '{sourcePath}'.", ex);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
