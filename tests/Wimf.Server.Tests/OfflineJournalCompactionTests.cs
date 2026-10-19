using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class OfflineJournalCompactionTests
{
    [Fact]
    public void CompactionKeepsPendingRecordsAndInvalidatesOldCursor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-compact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = Path.Combine(dir, "catalog.txt");
            var journal = Path.Combine(dir, "events.events");
            File.WriteAllText(catalog, "catalog");
            File.WriteAllText(journal, "S\tstart\t2026-10-19T00:00:00Z\n");
            var acknowledged = IncrementalJournalPublisher.SnapshotLength(journal);
            IncrementalJournalPublisher.AcknowledgeFullScan(catalog, journal, acknowledged);
            const string pending = "E\tcreated\tYQ==\n";
            File.AppendAllText(journal, pending);

            Assert.True(OfflineJournalCompaction.TryCompact(catalog, journal, minimumBytes: 1));
            Assert.Equal(pending, File.ReadAllText(journal));
            var storage = JournalStorageStatus.Read(catalog, journal);
            Assert.False(storage.CursorValid);
            Assert.Equal(storage.Bytes, storage.PendingBytes);
            Assert.False(OfflineJournalCompaction.TryCompact(catalog, journal, minimumBytes: 1));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingCursorPreventsCompaction()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-compact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = Path.Combine(dir, "catalog.txt");
            var journal = Path.Combine(dir, "events.events");
            File.WriteAllText(catalog, "catalog");
            File.WriteAllText(journal, "E\tcreated\tYQ==\n");
            Assert.False(OfflineJournalCompaction.TryCompact(catalog, journal, minimumBytes: 0));
            Assert.Contains("E\tcreated", File.ReadAllText(journal));
        }
        finally { Directory.Delete(dir, true); }
    }
}
