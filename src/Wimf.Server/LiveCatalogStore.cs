using System.Threading;
using Wimf.Catalog;
using FileCatalog = Wimf.Catalog.Catalog;

namespace Wimf.Server;

/// <summary>A search sees one stable snapshot while refresh builds a replacement.</summary>
public sealed class LiveCatalogStore
{
    private FileCatalog?[] _localCatalogs;
    private readonly string[] _localPaths;
    private (long Ticks, long Length)[] _localStamps;
    private readonly string[] _sources;
    private readonly string? _cacheDirectory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private FileCatalog?[] _sourceCatalogs;
    private FileCatalog[] _snapshot;
    private RefreshSummary? _lastSummary;

    public LiveCatalogStore(IEnumerable<FileCatalog> fixedCatalogs, IEnumerable<string> sources,
        string? cacheDirectory)
    {
        _localCatalogs = fixedCatalogs.Cast<FileCatalog?>().ToArray();
        _localPaths = [];
        _localStamps = [];
        _sources = sources.ToArray();
        _cacheDirectory = cacheDirectory;
        if (_sources.Length > 0 && string.IsNullOrWhiteSpace(cacheDirectory))
            throw new ArgumentException("A cache directory is required for sources.", nameof(cacheDirectory));
        _sourceCatalogs = new FileCatalog?[_sources.Length];
        _snapshot = _localCatalogs.OfType<FileCatalog>().ToArray();
    }


    /// <summary>Read local catalogs on startup, then reload changed files on refresh.</summary>
    public LiveCatalogStore(IEnumerable<string> localPaths, IEnumerable<string> sources,
        string? cacheDirectory, bool loadLocalFiles,
        IEnumerable<string>? allowMissingCatalogs = null,
        bool tolerateUnavailableCatalogs = false)
        : this(Array.Empty<FileCatalog>(), sources, cacheDirectory)
    {
        if (!loadLocalFiles) throw new ArgumentException("Use the existing snapshot constructor for preloaded catalogs.", nameof(loadLocalFiles));
        _localPaths = localPaths.ToArray();
        _localCatalogs = new FileCatalog?[_localPaths.Length];
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var allowedMissing = new HashSet<string>(allowMissingCatalogs ?? [], comparer);
        _localStamps = new (long, long)[_localPaths.Length];
        for (var i = 0; i < _localPaths.Length; i++)
        {
            try
            {
                if (!File.Exists(_localPaths[i]) &&
                    (tolerateUnavailableCatalogs || allowedMissing.Contains(Path.GetFullPath(_localPaths[i]))))
                {
                    Console.Error.WriteLine($"Catalog unavailable at startup; will retry: '{_localPaths[i]}'.");
                    continue;
                }
                _localCatalogs[i] = LegacyCatalogReader.ReadFile(_localPaths[i]);
                _localStamps[i] = GetStamp(_localPaths[i]);
            }
            catch (Exception ex) when (tolerateUnavailableCatalogs &&
                (ex is UnauthorizedAccessException ||
                 (ex is IOException && ex is not InvalidDataException)))
            {
                // An offline share must not block searches in other catalogs.
                Console.Error.WriteLine($"Catalog unavailable at startup; will retry '{_localPaths[i]}': {ex.Message}");
            }
        }
        _snapshot = _localCatalogs.OfType<FileCatalog>().ToArray();
    }

    private static (long Ticks, long Length) GetStamp(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists) throw new FileNotFoundException("Local catalog no longer exists.", path);
        return (file.LastWriteTimeUtc.Ticks, file.Length);
    }

    public FileCatalog[] Snapshot => Volatile.Read(ref _snapshot);
    public DateTimeOffset? LastRefresh => Volatile.Read(ref _lastSummary)?.CompletedAt;
    public RefreshSummary? LastSummary => Volatile.Read(ref _lastSummary);
    public int SourceCount => _sources.Length;
    public int LocalCount => _localCatalogs.Length;
    /// <summary>Configured local catalog paths, including unavailable ones.</summary>
    public (string Path, FileCatalog? Catalog)[] LocalCatalogStatus
    {
        get
        {
            var catalogs = Volatile.Read(ref _localCatalogs);
            return _localPaths.Select((path, i) => (path, catalogs[i])).ToArray();
        }
    }


    /// <summary>Reload one generated local catalog without contacting unrelated sources.</summary>
    public async Task<RefreshSummary> RefreshLocalCatalogAsync(string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var fullPath = Path.GetFullPath(path);
        var index = Array.FindIndex(_localPaths, configured => comparer.Equals(configured, fullPath));
        if (index < 0) throw new ArgumentException("Catalog is not configured as a local catalog.", nameof(path));

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = (FileCatalog?[])_localCatalogs.Clone();
            var stamps = ((long Ticks, long Length)[])_localStamps.Clone();
            var failedLocal = 0;
            try
            {
                var stamp = GetStamp(fullPath);
                if (next[index] is null || stamps[index] != stamp)
                {
                    var loaded = LegacyCatalogReader.ReadFile(fullPath);
                    if (GetStamp(fullPath) != stamp)
                        throw new IOException("Local catalog changed while loading.");
                    next[index] = loaded;
                    stamps[index] = stamp;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       System.Text.DecoderFallbackException)
            {
                failedLocal = 1;
                Console.Error.WriteLine($"Unable to refresh local catalog '{fullPath}': {ex.Message}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _localCatalogs = next;
            _localStamps = stamps;
            var snapshot = next.OfType<FileCatalog>().Concat(_sourceCatalogs.OfType<FileCatalog>()).ToArray();
            var previous = Volatile.Read(ref _snapshot);
            if (!previous.AsSpan().SequenceEqual(snapshot))
                Volatile.Write(ref _snapshot, snapshot);

            // Preserve unrelated failure indicators: those sources were not retried here.
            var preceding = Volatile.Read(ref _lastSummary);
            var summary = new RefreshSummary(snapshot.Length, _sources.Length,
                preceding?.FailedSources ?? 0, DateTimeOffset.UtcNow,
                failedLocal + Math.Max(0, (preceding?.FailedLocalCatalogs ?? 0) -
                    (preceding?.FailedLocalCatalogs > 0 ? 1 : 0)));
            Volatile.Write(ref _lastSummary, summary);
            return summary;
        }
        finally { _refreshLock.Release(); }
    }

    /// <summary>Serializes updates; leaves working source entries intact if refresh fails.</summary>
    public async Task<RefreshSummary> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var nextLocal = (FileCatalog?[])_localCatalogs.Clone();
            var nextStamps = ((long Ticks, long Length)[])_localStamps.Clone();
            var localFailures = 0;
            for (var i = 0; i < _localPaths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var stamp = GetStamp(_localPaths[i]);
                    if (nextLocal[i] is not null && stamp == nextStamps[i]) continue;
                    var loaded = LegacyCatalogReader.ReadFile(_localPaths[i]);
                    // A writer replacing the file during read must not publish a partial view.
                    if (GetStamp(_localPaths[i]) != stamp)
                        throw new IOException("Local catalog changed while loading.");
                    nextLocal[i] = loaded;
                    nextStamps[i] = stamp;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           InvalidDataException or ArgumentException or
                                           System.Text.DecoderFallbackException)
                {
                    localFailures++;
                    Console.Error.WriteLine($"Unable to refresh local catalog '{_localPaths[i]}': {ex.Message}");
                }
            }
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

            var snapshot = nextLocal.OfType<FileCatalog>().Concat(next.OfType<FileCatalog>()).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            _localCatalogs = nextLocal;
            _localStamps = nextStamps;
            _sourceCatalogs = next;
            // No need to publish a new array if all catalog instances are unchanged.
            var previous = Volatile.Read(ref _snapshot);
            if (!previous.AsSpan().SequenceEqual(snapshot))
                Volatile.Write(ref _snapshot, snapshot);
            var summary = new RefreshSummary(snapshot.Length, _sources.Length, failures, DateTimeOffset.UtcNow, localFailures);
            Volatile.Write(ref _lastSummary, summary);
            return summary;
        }
        finally { _refreshLock.Release(); }
    }
}

public sealed record RefreshSummary(int CatalogCount, int SourceCount, int FailedSources,
    DateTimeOffset CompletedAt, int FailedLocalCatalogs = 0);
