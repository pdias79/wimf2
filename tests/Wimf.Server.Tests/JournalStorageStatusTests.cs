using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class JournalStorageStatusTests
{
    [Fact]
    public void MissingCursorDoesNotTreatJournalAsAcknowledged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = Path.Combine(dir, "catalog.txt");
            var journal = Path.Combine(dir, "journal.events");
            File.WriteAllText(catalog, "catalog");
            File.WriteAllText(journal, "E\tcreated\tYQ==\n");
            var snapshot = JournalStorageStatus.Read(catalog, journal);
            Assert.False(snapshot.CursorValid);
            Assert.Equal(0, snapshot.AcknowledgedBytes);
            Assert.Equal(snapshot.Bytes, snapshot.PendingBytes);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FullScanAcknowledgementMakesCursorVisibleWithoutDeletingJournal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = Path.Combine(dir, "catalog.txt");
            var journal = Path.Combine(dir, "journal.events");
            File.WriteAllText(catalog, "catalog");
            File.WriteAllText(journal, "S\tstart\t2026-10-19T00:00:00Z\n");
            var boundary = IncrementalJournalPublisher.SnapshotLength(journal);
            IncrementalJournalPublisher.AcknowledgeFullScan(catalog, journal, boundary);
            var snapshot = JournalStorageStatus.Read(catalog, journal);
            Assert.True(snapshot.CursorValid);
            Assert.Equal(boundary, snapshot.AcknowledgedBytes);
            Assert.Equal(0, snapshot.PendingBytes);
            File.AppendAllText(journal, "S\tend\t2026-10-19T00:01:00Z\n");
            snapshot = JournalStorageStatus.Read(catalog, journal);
            Assert.True(snapshot.PendingBytes > 0);
        }
        finally { Directory.Delete(dir, true); }
    }
}
