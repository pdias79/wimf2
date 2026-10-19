using System.Security.Cryptography;
using System.Text;

namespace Wimf.Server;

/// <summary>
/// Conservative persistent SMB notification hints. Never proves a share is in sync.
/// An explicit full-catalog reconciliation is needed to clear rescan requirements.
/// </summary>
public sealed class SmbBackgroundWatcher : IDisposable
{
    private const int MaxDirectories = 2000;
    private readonly FileSystemWatcher _watcher;
    private readonly string _root;
    private readonly string _journal;
    private readonly object _sync = new();
    private readonly HashSet<string> _directories =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private FileStream? _stream;
    private long _events;
    private long _errors;
    private bool _needsFullScan = true; // No durable proof of uninterrupted coverage.
    private bool _disposed;
    private bool _watchFailed;
    private readonly string _stateDirectory;

    public static string GetJournalPath(string root, string stateDirectory)
    {
        var normalized = Path.GetFullPath(root);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())));
        return Path.Combine(stateDirectory, hash + ".events");
    }

    public SmbBackgroundWatcher(string root, string stateDirectory)
    {
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException($"Watched root unavailable: {_root}");
        Directory.CreateDirectory(stateDirectory);
        _stateDirectory = Path.GetFullPath(stateDirectory);
        _journal = GetJournalPath(_root, stateDirectory);
        if (Path.GetFullPath(_journal).StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            !Path.GetPathRoot(_root)!.Equals(_root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("The journal must be outside the watched root.", nameof(stateDirectory));

        // Replaying the hints is diagnostic only: a restart always requires full reconciliation.
        if (File.Exists(_journal))
        {
            foreach (var line in File.ReadLines(_journal))
            {
                if (!line.StartsWith("D\t", StringComparison.Ordinal)) continue;
                try { AddDirectory(Encoding.UTF8.GetString(Convert.FromBase64String(line[2..]))); }
                catch (FormatException) { _errors++; }
            }
        }
        _stream = new FileStream(_journal, FileMode.Append, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024
        };
        _watcher.Created += OnChange;
        _watcher.Changed += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnRename;
        _watcher.Error += OnError;
    }

    public string Root => _root;
    public string JournalPath => _journal;
    public long Observed => Interlocked.Read(ref _events);
    public long Errors => Interlocked.Read(ref _errors);
    public bool NeedsFullScan => _needsFullScan;
    public bool WatchFailed => _watchFailed;
    public string[] AffectedDirectories
    {
        get { lock (_sync) return _directories.OrderBy(x => x, StringComparer.Ordinal).ToArray(); }
    }

    private void Marker(string kind)
    {
        lock (_sync)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes("S\t" + kind + "\t" +
                    DateTimeOffset.UtcNow.ToString("O") + "\n");
                _stream!.Write(bytes);
                _stream.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                _needsFullScan = true;
                Interlocked.Increment(ref _errors);
                Console.Error.WriteLine($"[SMB-WATCH-ERROR] Marker write failed: {ex.Message}");
            }
        }
    }

    public void Start()
    {
        Marker("start");
        _watcher.EnableRaisingEvents = true;
        Console.WriteLine($"[SMB-WATCH] started | {_root} | persisted hints; full rescan required");
    }

    private void AddDirectory(string path)
    {
        if (_directories.Count >= MaxDirectories && !_directories.Contains(path))
        {
            _needsFullScan = true;
            return;
        }
        _directories.Add(path);
    }

    private void Remember(string path, string action, string kind = "unknown")
    {
        // For whole-volume watches, the journal is necessarily on the same volume.
        // Never emit notifications about our own persistent journal directory.
        if (path.StartsWith(_stateDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        Interlocked.Increment(ref _events);
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) directory = _root;
        lock (_sync)
        {
            AddDirectory(directory);
            try
            {
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(directory));
                var exact = Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
                var bytes = Encoding.UTF8.GetBytes("D\t" + encoded + "\n" +
                    "E\t" + action + "\t" + exact + "\n" +
                    "K\t" + action + "\t" + kind + "\t" + exact + "\n");
                _stream!.Write(bytes);
                _stream.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                _needsFullScan = true;
                Interlocked.Increment(ref _errors);
                Console.Error.WriteLine($"[SMB-WATCH-ERROR] {_root}: journal write failed: {ex.Message}; full rescan required.");
            }
        }
    }

    private static string DetectKind(string path)
    {
        // A deleted/renamed-away path cannot be reliably typed from the filesystem.
        if (Directory.Exists(path)) return "directory";
        if (File.Exists(path)) return "file";
        return "unknown";
    }

    private void OnChange(object sender, FileSystemEventArgs e) =>
        Remember(e.FullPath, e.ChangeType.ToString().ToLowerInvariant(),
            e.ChangeType == WatcherChangeTypes.Deleted ? "unknown" : DetectKind(e.FullPath));
    private void OnRename(object sender, RenamedEventArgs e)
    {
        Remember(e.OldFullPath, "renamed-from");
        Remember(e.FullPath, "renamed-to", DetectKind(e.FullPath));
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        _watchFailed = true;
        _needsFullScan = true;
        Marker("error");
        Interlocked.Increment(ref _errors);
        Console.Error.WriteLine($"[SMB-WATCH-ERROR] {_root}: {e.GetException().Message}; full rescan required.");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _watcher.Dispose();
            Marker("end");
            _stream?.Dispose();
        }
        Console.WriteLine($"[SMB-WATCH] stopped | {_root} | events={Observed} | directories={AffectedDirectories.Length} | errors={Errors} | fullScanRequired={NeedsFullScan}");
    }
}
