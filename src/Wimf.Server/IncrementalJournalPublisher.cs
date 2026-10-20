using System.Security.Cryptography;
using System.Text;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Persists journal acknowledgement only after the new catalog is published.</summary>
public static class IncrementalJournalPublisher
{
    public sealed record Result(int Entries, int Hints, long AcknowledgedBytes, bool Changed,
        int EventRecords = 0, int DistinctPaths = 0, long ElapsedMilliseconds = 0);

    public sealed record PendingResult(int Events, int Directories);

    // Bound journal reads so a long-running watcher cannot exhaust memory.
    internal const int MaxBatchBytes = 8 * 1024 * 1024;

    public static PendingResult Pending(string catalog, string journal)
    {
        if (!File.Exists(journal)) return new(0, 0);
        var target = Path.GetFullPath(catalog);
        var marker = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + ".incremental-cursor");
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(journal).ToUpperInvariant())));
        var offset = 0L;
        if (File.Exists(target) && File.Exists(marker))
        {
            var parts = File.ReadAllText(marker).Split('\t');
            if (parts.Length == 3 && parts[0] == signature &&
                long.TryParse(parts[1], out var acknowledged) &&
                long.TryParse(parts[2], out var ticks) &&
                ticks == File.GetLastWriteTimeUtc(target).Ticks)
                offset = acknowledged;
        }
        using var stream = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (offset < 0 || offset > stream.Length) offset = 0;
        var completeEnd = CompleteBoundary(stream, offset);
        if (offset == completeEnd) return new(0, 0);
        var directories = new HashSet<string>(OperatingSystem.IsWindows() ?
            StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var events = 0;
        while (offset < completeEnd)
        {
            var batchEnd = completeEnd - offset > MaxBatchBytes
                ? BatchBoundary(stream, offset, completeEnd) : completeEnd;
            stream.Position = offset;
            var snapshot = new byte[checked((int)(batchEnd - offset))];
            stream.ReadExactly(snapshot);
            using var bounded = new MemoryStream(snapshot, writable: false);
            using var reader = new StreamReader(bounded, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("E\t", StringComparison.Ordinal)) events++;
                if (line.StartsWith("D\t", StringComparison.Ordinal))
                    directories.Add(line[2..]);
            }
            offset = batchEnd;
        }
        return new(events, directories.Count);
    }

    /// <summary>After a full scan, acknowledge only the events already present
    /// before that scan began. Events recorded during the scan remain pending.</summary>
    public static long SnapshotLength(string journal)
    {
        if (!File.Exists(journal)) return 0;
        using var stream = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return CompleteBoundary(stream, 0);
    }

    // Return the byte offset immediately after the last complete line.
    // Never consume a partially appended record.
    private static long CompleteBoundary(FileStream stream, long lowerBound)
    {
        if (stream.Length <= lowerBound) return lowerBound;
        var offset = stream.Length;
        var buffer = new byte[4096];
        while (offset > lowerBound)
        {
            var count = (int)Math.Min(buffer.Length, offset - lowerBound);
            offset -= count;
            stream.Position = offset;
            stream.ReadExactly(buffer.AsSpan(0, count));
            for (var i = count - 1; i >= 0; i--)
                if (buffer[i] == (byte)'\n') return offset + i + 1;
        }
        return lowerBound;
    }

    // Stop at a complete record line at or below the maximum batch size.
    // If one line exceeds the budget, fail closed without advancing the cursor.
    private static long BatchBoundary(FileStream stream, long start, long fullEnd)
    {
        var limit = Math.Min(fullEnd, checked(start + MaxBatchBytes));
        var buffer = new byte[64 * 1024];
        var cursor = start;
        var lastComplete = start;
        while (cursor < limit)
        {
            var count = (int)Math.Min(buffer.Length, limit - cursor);
            stream.Position = cursor;
            stream.ReadExactly(buffer.AsSpan(0, count));
            for (var i = 0; i < count; i++)
                if (buffer[i] == (byte)'\n') lastComplete = cursor + i + 1;
            cursor += count;
        }
        if (lastComplete == start)
            throw new InvalidDataException("A single journal record exceeds the batch budget.");
        return lastComplete;
    }

    public static bool AcknowledgeFullScan(string catalog, string journal, long beforeScanLength)
    {
        if (!File.Exists(catalog) || !File.Exists(journal)) return false;
        var target = Path.GetFullPath(catalog);
        var currentLength = new FileInfo(journal).Length;
        // If journal was truncated/replaced, replay rather than silently lose events.
        if (beforeScanLength < 0 || beforeScanLength > currentLength) return false;
        var marker = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + ".incremental-cursor");
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(journal).ToUpperInvariant())));
        var temp = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, signature + "\t" + beforeScanLength + "\t" +
                File.GetLastWriteTimeUtc(target).Ticks);
            File.Move(temp, marker, overwrite: true);
            return true;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static Result Publish(string root, string catalog, string journal, CancellationToken token = default,
        IEnumerable<string>? exclusions = null, Action? afterJournalSnapshot = null)
    {
        if (!File.Exists(catalog) || !File.Exists(journal))
            return new(0, 0, 0, false);
        // Do not even start projecting deletions when a share has disconnected.
        // A successful full or incremental publication must observe an accessible root.
        EnsureRootAccessible(root);
        var target = Path.GetFullPath(catalog);
        var originalStamp = CatalogFileStamp.Read(target);
        var marker = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + ".incremental-cursor");
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(journal).ToUpperInvariant())));
        var position = 0L;
        if (File.Exists(marker))
        {
            var parts = File.ReadAllText(marker).Split('\t');
            if (parts.Length == 3 && parts[0] == signature &&
                long.TryParse(parts[1], out var parsed) &&
                long.TryParse(parts[2], out var ticks) &&
                File.GetLastWriteTimeUtc(target).Ticks == ticks)
                position = parsed;
        }
        using var stream = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        // A truncated or replaced journal invalidates the previous byte offset.
        if (position < 0 || position > stream.Length) position = 0;
        var end = CompleteBoundary(stream, position);
        if (end - position > MaxBatchBytes)
            end = BatchBoundary(stream, position, end);
        // Test seam for deterministic concurrent appends. The captured end is
        // immutable: notifications written afterwards belong to the next batch.
        afterJournalSnapshot?.Invoke();
        if (position > end) position = 0; // Journal rotation or truncation.
        if (position == end) return new(0, 0, position, false);
        // Snapshot the journal boundary before doing expensive work. Append-only watcher
        // events arriving after this boundary stay pending for the next run.
        var staging = Path.Combine(Path.GetDirectoryName(target)!,
            ".wimf-incremental-" + Guid.NewGuid().ToString("N") +
            (target.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? ".txt.gz" : ".txt"));
        try
        {
            var result = JournalIncrementalRefresh.Run(root, target, journal, staging, position, end, token, exclusions);
            token.ThrowIfCancellationRequested();
            EnsureRootAccessible(root);
            if (!CatalogFileStamp.Read(target).Equals(originalStamp))
                throw new IOException("Catalog changed during incremental preparation; publication refused.");
            if (result.Hints == 0)
                File.Delete(staging);
            else
                File.Move(staging, target, overwrite: true);
            // An ack failure can replay the batch; replay is idempotent.
            var tempMarker = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempMarker, signature + "\t" + end + "\t" +
                    File.GetLastWriteTimeUtc(target).Ticks);
                File.Move(tempMarker, marker, overwrite: true);
            }
            finally { if (File.Exists(tempMarker)) File.Delete(tempMarker); }
            return new(result.Entries, result.Hints, end, result.Hints != 0,
                result.EventRecords, result.DistinctPaths, result.ElapsedMilliseconds);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    private static void EnsureRootAccessible(string root)
    {
        try
        {
            using var iterator = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
            _ = iterator.MoveNext();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or DirectoryNotFoundException or System.Security.SecurityException)
        {
            throw new InvalidDataException("Root became inaccessible; incremental publication refused.", ex);
        }
    }
}
