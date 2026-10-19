using System.Security.Cryptography;
using System.Text;

namespace Wimf.Server;

/// <summary>
/// Offline, conservative journal compaction. Call only before starting the watcher.
/// The acknowledged prefix is discarded; unacknowledged bytes are copied verbatim.
/// </summary>
public static class OfflineJournalCompaction
{
    public const long MinimumAcknowledgedBytes = 64L * 1024 * 1024;

    public static bool TryCompact(string catalog, string journal, long minimumBytes = MinimumAcknowledgedBytes)
    {
        if (minimumBytes < 0) throw new ArgumentOutOfRangeException(nameof(minimumBytes));
        var status = JournalStorageStatus.Read(catalog, journal);
        if (!status.CursorValid || status.AcknowledgedBytes < minimumBytes) return false;
        var target = Path.GetFullPath(catalog);
        var marker = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + ".incremental-cursor");
        var journalPath = Path.GetFullPath(journal);
        var staging = journalPath + "." + Guid.NewGuid().ToString("N") + ".compact-tmp";
        // A competing writer must never be allowed to append while we copy.
        using (var source = new FileStream(journalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Re-check under exclusive ownership: the journal could have changed
            // after the first observation.
            status = JournalStorageStatus.Read(catalog, journalPath);
            if (!status.CursorValid || status.AcknowledgedBytes < minimumBytes) return false;
            source.Position = status.AcknowledgedBytes;
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            // Invalidate the old byte offset BEFORE replacing the journal.
            // A crash at any point can only cause reprocessing, not lost hints.
            File.Delete(marker);
            source.SetLength(0);
            using var tail = new FileStream(staging, FileMode.Open, FileAccess.Read, FileShare.Read);
            tail.CopyTo(source);
            source.Flush(flushToDisk: true);
        }
        File.Delete(staging);
        return true;
    }
}
