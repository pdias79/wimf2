using System.Text;
using System.Collections.Concurrent;

namespace Wimf.Server;

/// <summary>
/// Cross-process / cross-host cooperative lock stored next to the catalog.
/// The operating system's byte-range lock, not a wall-clock timestamp, is authoritative.
/// SMB implementations that cannot grant a lock cause a fail-closed indexing attempt.
/// </summary>
public sealed class SharedCatalogLock : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> HeldLocally = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly FileStream _stream;
    private bool _disposed;
    public string LockPath { get; }

    private SharedCatalogLock(FileStream stream, string path)
    {
        _stream = stream;
        LockPath = path;
    }

    public static SharedCatalogLock? TryAcquire(string catalogPath, TimeSpan staleAfter)
    {
        if (staleAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(staleAfter));
        var path = Path.GetFullPath(catalogPath) + ".wimf.lock";
        // Linux fcntl locks can be process-wide, so guard independent holders
        // inside this process as well as taking the network-visible OS lock.
        if (!HeldLocally.TryAdd(path, 0)) return null;
        // Never delete the lock file: another host might already have it open.
        // A surviving marker is harmless after a crash, since the OS releases its lock.
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.ReadWrite, 4096, FileOptions.None);
            try { stream.Lock(0, 1); }
            catch (IOException) { stream.Dispose(); HeldLocally.TryRemove(path, out _); return null; }
            // Metadata is informational. A timed-out owner cannot be evicted while
            // its kernel/SMB lock is still held, including when a scan exceeds the TTL.
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age >= staleAfter)
                Console.WriteLine($"[CATALOG-LOCK] replacing stale owner marker | {path} | age={age}");
            var marker = $"owner={Environment.MachineName}/{Environment.ProcessId}\\n" +
                         $"acquiredUtc={DateTimeOffset.UtcNow:O}\\n";
            stream.SetLength(0);
            var bytes = Encoding.UTF8.GetBytes(marker);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return new SharedCatalogLock(stream, path);
        }
        catch
        {
            stream?.Dispose();
            HeldLocally.TryRemove(path, out _);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _stream.Unlock(0, 1); }
        finally
        {
            _stream.Dispose();
            HeldLocally.TryRemove(LockPath, out _);
        }
    }
}
