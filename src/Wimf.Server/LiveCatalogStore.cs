using System.Threading;
using Wimf.Catalog;
using FileCatalog = Wimf.Catalog.Catalog;

namespace Wimf.Server;

/// <summary>A search sees one stable snapshot while refresh builds a replacement.</summary>
public sealed class LiveCatalogStore
{
    private readonly FileCatalog[] _fixedCatalogs;
    private readonly string[] _sources;
    private readonly string? _cacheDirectory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private FileCatalog?[] _sourceCatalogs;
    private FileCatalog[] _snapshot;
    private RefreshSummary? _lastSummary;

    public LiveCatalogStore(IEnumerable<FileCatalog> fixedCatalogs, IEnumerable<string> sources,
        string? cacheDirectory)
    {
        _fixedCatalogs = fixedCatalogs.ToArray();
        _sources = sources.ToArray();
        _cacheDirectory = cacheDirectory;
        if (_sources.Length > 0 && string.IsNullOrWhiteSpace(cacheDirectory))
            throw new ArgumentException("A cache directory is required for sources.", nameof(cacheDirectory));
        _sourceCatalogs = new FileCatalog?[_sources.Length];
        _snapshot = _fixedCatalogs.ToArray();
    }

    public FileCatalog[] Snapshot => Volatile.Read(ref _snapshot);
    public DateTimeOffset? LastRefresh => Volatile.Read(ref _lastSummary)?.CompletedAt;
    public int SourceCount => _sources.Length;

    /// <summary>Serializes updates; leaves working source entries intact if refresh fails.</summary>
    public async Task<RefreshSummary> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var next = (FileCatalog?[])_sourceCatalogs.Clone();
            var failures = 0;
            for (int i = 0; i < _sources.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var (catalog, fallback) = CatalogCache.Load(_sources[i], _cacheDirectory!,
                        previouslyLoaded: next[i]);
                    next[i] = catalog;
                    if (fallback) failures++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           InvalidDataException or ArgumentException)
                {
                    failures++;
                    // Preserve the catalog already published for this source.
                    Console.Error.WriteLine($"Unable to refresh source '{_sources[i]}': {ex.Message}");
                }
            }

            var snapshot = _fixedCatalogs.Concat(next.OfType<FileCatalog>()).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            _sourceCatalogs = next;
            // No need to publish a new array if all catalog instances are unchanged.
            var previous = Volatile.Read(ref _snapshot);
            if (!previous.AsSpan().SequenceEqual(snapshot))
                Volatile.Write(ref _snapshot, snapshot);
            var summary = new RefreshSummary(snapshot.Length, _sources.Length, failures, DateTimeOffset.UtcNow);
            Volatile.Write(ref _lastSummary, summary);
            return summary;
        }
        finally { _refreshLock.Release(); }
    }
}

public sealed record RefreshSummary(int CatalogCount, int SourceCount, int FailedSources,
    DateTimeOffset CompletedAt);
