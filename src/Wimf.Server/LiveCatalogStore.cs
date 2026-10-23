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
    private bool[] _localFailures;
    private readonly string[] _sources;
    private readonly string? _cacheDirectory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private FileCatalog?[] _sourceCatalogs;
    private FileCatalog[] _snapshot;
    private RefreshSummary? _lastSummary;
    private volatile bool _unloaded;

    public LiveCatalogStore(IEnumerable<FileCatalog> fixedCatalogs, IEnumerable<string> sources,
        string? cacheDirectory)
    {
        _localCatalogs = fixedCatalogs.Cast<FileCatalog?>().ToArray();
        _localPaths = [];
        _localStamps = [];
        _localFailures = [];
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
        _unloaded = !loadLocalFiles;
        _localPaths = localPaths.ToArray();
        _localCatalogs = new FileCatalog?[_localPaths.Length];
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var allowedMissing = new HashSet<string>(allowMissingCatalogs ?? [], comparer);
        _localStamps = new (long, long)[_localPaths.Length];
        _localFailures = new bool[_localPaths.Length];
        for (var i = 0; loadLocalFiles && i < _localPaths.Length; i++)
        {
            try
            {
                if (!IsNetworkPath(_localPaths[i]) && !File.Exists(_localPaths[i]) &&
                    (tolerateUnavailableCatalogs || allowedMissing.Contains(Path.GetFullPath(_localPaths[i]))))
                {
                    _localFailures[i] = true;
                    Console.Error.WriteLine($"Catalog unavailable at startup; will retry: '{_localPaths[i]}'.");
                    continue;
                }
                _localCatalogs[i] = LoadLocalCatalog(_localPaths[i]);
                _localStamps[i] = IsNetworkPath(_localPaths[i]) ? default : GetStamp(_localPaths[i]);
            }
            catch (Exception ex) when (tolerateUnavailableCatalogs &&
                (ex is UnauthorizedAccessException ||
                 (ex is IOException && ex is not InvalidDataException)))
            {
                // An offline share must not block searches in other catalogs.
                _localFailures[i] = true;
                Console.Error.WriteLine($"Catalog unavailable at startup; will retry '{_localPaths[i]}': {ex.Message}");
            }
        }
        _snapshot = _localCatalogs.OfType<FileCatalog>().ToArray();
    }

    private static bool IsNetworkPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal);

    private FileCatalog LoadLocalCatalog(string path, FileCatalog? previous = null)
    {
        if (!IsNetworkPath(path)) return LegacyCatalogReader.ReadFile(path);
        var directory = _cacheDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Wimf2", "CatalogCache");
        return CatalogCache.Load(path, directory, previouslyLoaded: previous).Catalog;
    }

    private static (long Ticks, long Length) GetStamp(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists) throw new FileNotFoundException("Local catalog no longer exists.", path);
        return (file.LastWriteTimeUtc.Ticks, file.Length);
    }

    public FileCatalog[] Snapshot => Volatile.Read(ref _snapshot);
    public bool IsUnloaded => _unloaded;

    /// <summary>Drop all store-held catalog references, without touching disk caches.</summary>
    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            _unloaded = true;
            _localCatalogs = new FileCatalog?[_localPaths.Length];
            _sourceCatalogs = new FileCatalog?[_sources.Length];
            Volatile.Write(ref _snapshot, Array.Empty<FileCatalog>());
        }
        finally { _refreshLock.Release(); }
    }

    /// <summary>Rehydrate from current catalog files, using persistent source caches.</summary>
    public async Task<RefreshSummary> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try { _unloaded = false; }
        finally { _refreshLock.Release(); }
        return await RefreshAsync(cancellationToken);
    }
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
            if (_unloaded) return Volatile.Read(ref _lastSummary) ??
                new RefreshSummary(0, _sources.Length, 0, DateTimeOffset.UtcNow);
            cancellationToken.ThrowIfCancellationRequested();
            var next = (FileCatalog?[])_localCatalogs.Clone();
            var stamps = ((long Ticks, long Length)[])_localStamps.Clone();
            var localFailures = (bool[])_localFailures.Clone();
            try
            {
                var stamp = IsNetworkPath(fullPath) ? default : GetStamp(fullPath);
                if (next[index] is null || stamps[index] != stamp)
                {
                    var loaded = LoadLocalCatalog(fullPath, next[index]);
                    if (!IsNetworkPath(fullPath) && GetStamp(fullPath) != stamp)
                        throw new IOException("Local catalog changed while loading.");
                    next[index] = loaded;
                    stamps[index] = stamp;
                }
                localFailures[index] = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or
                                       System.Text.DecoderFallbackException)
            {
                localFailures[index] = true;
                Console.Error.WriteLine($"Unable to refresh local catalog '{fullPath}': {ex.Message}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _localCatalogs = next;
            _localStamps = stamps;
            _localFailures = localFailures;
            var snapshot = next.OfType<FileCatalog>().Concat(_sourceCatalogs.OfType<FileCatalog>()).ToArray();
            var previous = Volatile.Read(ref _snapshot);
            if (!previous.AsSpan().SequenceEqual(snapshot))
                Volatile.Write(ref _snapshot, snapshot);

            // Preserve unrelated failure indicators: those sources were not retried here.
            var preceding = Volatile.Read(ref _lastSummary);
            var summary = new RefreshSummary(snapshot.Length, _sources.Length,
                preceding?.FailedSources ?? 0, DateTimeOffset.UtcNow, localFailures.Count(failed => failed));
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
            if (_unloaded) return Volatile.Read(ref _lastSummary) ??
                new RefreshSummary(0, _sources.Length, 0, DateTimeOffset.UtcNow);
            var nextLocal = (FileCatalog?[])_localCatalogs.Clone();
            var nextStamps = ((long Ticks, long Length)[])_localStamps.Clone();
            var localFailures = (bool[])_localFailures.Clone();
            for (var i = 0; i < _localPaths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var stamp = IsNetworkPath(_localPaths[i]) ? default : GetStamp(_localPaths[i]);
                    if (!IsNetworkPath(_localPaths[i]) && nextLocal[i] is not null && stamp == nextStamps[i])
                    {
                        localFailures[i] = false;
                        continue;
                    }
                    var loaded = LoadLocalCatalog(_localPaths[i], nextLocal[i]);
                    // A writer replacing the file during read must not publish a partial view.
                    if (!IsNetworkPath(_localPaths[i]) && GetStamp(_localPaths[i]) != stamp)
                        throw new IOException("Local catalog changed while loading.");
                    nextLocal[i] = loaded;
                    nextStamps[i] = stamp;
                    localFailures[i] = false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           InvalidDataException or ArgumentException or
                                           System.Text.DecoderFallbackException)
                {
                    localFailures[i] = true;
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
            _localFailures = localFailures;
            _sourceCatalogs = next;
            // No need to publish a new array if all catalog instances are unchanged.
            var previous = Volatile.Read(ref _snapshot);
            if (!previous.AsSpan().SequenceEqual(snapshot))
                Volatile.Write(ref _snapshot, snapshot);
            var summary = new RefreshSummary(snapshot.Length, _sources.Length, failures, DateTimeOffset.UtcNow, localFailures.Count(failed => failed));
            Volatile.Write(ref _lastSummary, summary);
            return summary;
        }
        finally { _refreshLock.Release(); }
    }
}

public sealed record RefreshSummary(int CatalogCount, int SourceCount, int FailedSources,
    DateTimeOffset CompletedAt, int FailedLocalCatalogs = 0);
