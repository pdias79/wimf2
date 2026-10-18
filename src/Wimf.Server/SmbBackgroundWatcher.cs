namespace Wimf.Server;

/// <summary>
/// Optional in-process SMB change diagnostics. Notifications are hints, not a
/// complete journal and never trigger catalog updates.
/// </summary>
public sealed class SmbBackgroundWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _root;
    private long _events;
    private long _errors;
    private bool _disposed;

    public SmbBackgroundWatcher(string root)
    {
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException($"Watched root unavailable: {_root}");
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
    public long Observed => Interlocked.Read(ref _events);
    public long Errors => Interlocked.Read(ref _errors);

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
        Console.WriteLine($"[SMB-WATCH] started | {_root} | diagnostic only");
    }

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref _events);
    }

    private void OnRename(object sender, RenamedEventArgs e)
    {
        Interlocked.Increment(ref _events);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        Interlocked.Increment(ref _errors);
        Console.Error.WriteLine($"[SMB-WATCH-ERROR] {_root}: {e.GetException().Message}; full rescan required.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.Dispose();
        Console.WriteLine($"[SMB-WATCH] stopped | {_root} | events={Observed} | errors={Errors}");
    }
}
