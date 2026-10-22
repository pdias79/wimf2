using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalCursorPathTests
{
    [Fact]
    public void TwoJournalsForOneCatalogHaveIndependentCursors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-local-cursor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = Path.Combine(dir, "shared", "catalog.gz");
            var a = Path.Combine(dir, "host-a", "events");
            var b = Path.Combine(dir, "host-b", "events");
            Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);
            File.WriteAllText(catalog, "catalog");
            File.WriteAllText(a, "S\tstart\t2026-10-19T00:00:00Z\n");
            File.WriteAllText(b, "S\tstart\t2026-10-19T00:00:00Z\n");
            Assert.NotEqual(IncrementalCursorPath.For(catalog, a), IncrementalCursorPath.For(catalog, b));
            Assert.True(IncrementalJournalPublisher.AcknowledgeFullScan(catalog, a,
                IncrementalJournalPublisher.SnapshotLength(a)));
            Assert.True(IncrementalJournalPublisher.AcknowledgeFullScan(catalog, b,
                IncrementalJournalPublisher.SnapshotLength(b)));
            Assert.True(JournalStorageStatus.Read(catalog, a).CursorValid);
            Assert.True(JournalStorageStatus.Read(catalog, b).CursorValid);
            Assert.True(File.Exists(IncrementalCursorPath.For(catalog, a)));
            Assert.True(File.Exists(IncrementalCursorPath.For(catalog, b)));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(catalog)!,
                "." + Path.GetFileName(catalog) + ".incremental-cursor")));
            Assert.True(OfflineJournalCompaction.TryCompact(catalog, a, minimumBytes: 1));
            Assert.False(JournalStorageStatus.Read(catalog, a).CursorValid);
            Assert.True(JournalStorageStatus.Read(catalog, b).CursorValid);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LegacySharedCursorIsNotTrustworthy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-legacy-cursor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = Path.Combine(dir, "catalog.gz");
            var journal = Path.Combine(dir, "events");
            File.WriteAllText(catalog, "catalog");
            File.WriteAllText(journal, "E\tcreated\tYQ==\n");
            var legacy = Path.Combine(dir, "." + Path.GetFileName(catalog) + ".incremental-cursor");
            File.WriteAllText(legacy, "arbitrary old marker");
            var status = JournalStorageStatus.Read(catalog, journal);
            Assert.False(status.CursorValid);
            Assert.Equal(status.Bytes, status.PendingBytes);
            Assert.Equal("arbitrary old marker", File.ReadAllText(legacy));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
