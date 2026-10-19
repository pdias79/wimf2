using System.Security.Cryptography;
using System.Text;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Persists journal acknowledgement only after the new catalog is published.</summary>
public static class IncrementalJournalPublisher
{
    public sealed record Result(int Entries, int Hints, long AcknowledgedBytes, bool Changed);

    public sealed record PendingResult(int Events, int Directories);

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
        if (offset > stream.Length) offset = 0;
        if (offset == stream.Length) return new(0, 0);
        stream.Position = offset;
        var directories = new HashSet<string>(OperatingSystem.IsWindows() ?
            StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var events = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("E\t", StringComparison.Ordinal)) events++;
            if (line.StartsWith("D\t", StringComparison.Ordinal))
                directories.Add(line[2..]);
        }
        return new(events, directories.Count);
    }

    /// <summary>After a full scan, acknowledge only the events already present
    /// before that scan began. Events recorded during the scan remain pending.</summary>
    public static long SnapshotLength(string journal) =>
        File.Exists(journal) ? new FileInfo(journal).Length : 0;

    public static void AcknowledgeFullScan(string catalog, string journal, long beforeScanLength)
    {
        if (!File.Exists(catalog) || !File.Exists(journal)) return;
        var target = Path.GetFullPath(catalog);
        var currentLength = new FileInfo(journal).Length;
        // If journal was truncated/replaced, replay rather than silently lose events.
        if (beforeScanLength < 0 || beforeScanLength > currentLength) return;
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
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static Result Publish(string root, string catalog, string journal, CancellationToken token = default)
    {
        if (!File.Exists(catalog) || !File.Exists(journal))
            return new(0, 0, 0, false);
        var target = Path.GetFullPath(catalog);
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
        var end = stream.Length;
        if (position > end) position = 0; // Journal rotation or truncation.
        if (position == end) return new(0, 0, position, false);
        // Snapshot the journal boundary before doing expensive work. Append-only watcher
        // events arriving after this boundary stay pending for the next run.
        var staging = Path.Combine(Path.GetDirectoryName(target)!,
            ".wimf-incremental-" + Guid.NewGuid().ToString("N") +
            (target.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? ".txt.gz" : ".txt"));
        try
        {
            var result = JournalIncrementalRefresh.Run(root, target, journal, staging, position, end, token);
            token.ThrowIfCancellationRequested();
            EnsureRootAccessible(root);
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
            return new(result.Entries, result.Hints, end, result.Hints != 0);
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
                                   or System.Security.SecurityException)
        {
            throw new InvalidDataException("Root became inaccessible; incremental publication refused.", ex);
        }
    }
}
