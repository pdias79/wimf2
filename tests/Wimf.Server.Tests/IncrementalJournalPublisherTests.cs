using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalJournalPublisherTests
{
    [Fact]
    public void PublishesOnceAndKeepsSubsequentJournalEventsPending()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-ack-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var journal = Path.Combine(temp, "journal.events");
            var first = Path.Combine(root, "first.txt");
            File.WriteAllText(first, "1");
            File.WriteAllText(journal, "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(first)) + "\n");
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var result = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.True(result.Changed);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            Assert.Single(LegacyCatalogReader.ReadFile(catalog).EnumerateEntries());
            Assert.False(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            var second = Path.Combine(root, "second.txt");
            File.WriteAllText(second, "2");
            File.AppendAllText(journal, "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(second)) + "\n");
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            Assert.True(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            Assert.Equal(2, LegacyCatalogReader.ReadFile(catalog).EnumerateEntries().Count());
        }
        finally { Directory.Delete(temp, true); }
    }
    [Fact]
    public void FullScanAcknowledgesOnlyEventsPresentBeforeItStarted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-full-boundary-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(directory, "catalog.txt.gz");
            var journal = Path.Combine(directory, "events");
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.txt");
            File.WriteAllText(first, "first");
            File.WriteAllText(journal, "E\tchanged\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(first)) + "\n");
            var boundary = IncrementalJournalPublisher.SnapshotLength(journal);
            File.WriteAllText(second, "second");
            File.AppendAllText(journal, "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(second)) + "\n");
            LegacyCatalogWriter.Build(root, catalog);
            IncrementalJournalPublisher.AcknowledgeFullScan(catalog, journal, boundary);
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            Assert.False(IncrementalJournalPublisher.Publish(root, catalog, journal).Hints == 0);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void IncompleteJournalRecordDoesNotPublishOrAcknowledge()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-partial-event-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var originalBytes = File.ReadAllBytes(catalog);
            var journal = Path.Combine(temp, "events");
            var first = Path.Combine(root, "new.txt");
            File.WriteAllText(first, "new");
            var eventRecord = "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(first)) + "\n";
            // A write interrupted before the terminating LF must not advance the cursor.
            File.WriteAllText(journal, eventRecord[..^1]);
            Assert.False(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            Assert.Equal(originalBytes, File.ReadAllBytes(catalog));
            Assert.Equal(0, IncrementalJournalPublisher.SnapshotLength(journal));
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            File.AppendAllText(journal, "\n");
            Assert.True(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            Assert.Single(LegacyCatalogReader.ReadFile(catalog).EnumerateEntries());
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void MissingRootNeverOverwritesExistingCatalog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-disconnected-root-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var originalBytes = File.ReadAllBytes(catalog);
            var journal = Path.Combine(temp, "events");
            var path = Path.Combine(root, "missing.txt");
            File.WriteAllText(journal, "E\tdeleted\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(path)) + "\n");
            Directory.Delete(root);
            Assert.ThrowsAny<Exception>(() =>
                IncrementalJournalPublisher.Publish(root, catalog, journal));
            Assert.Equal(originalBytes, File.ReadAllBytes(catalog));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void MalformedMetadataMustNotAdvanceAcknowledgement()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-bad-event-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var original = File.ReadAllBytes(catalog);
            var journal = Path.Combine(temp, "events");
            File.WriteAllText(journal, "D\n");
            Assert.Throws<InvalidDataException>(() =>
                IncrementalJournalPublisher.Publish(root, catalog, journal));
            Assert.Equal(original, File.ReadAllBytes(catalog));
            File.WriteAllText(journal, "S\tstart\t2026-10-19T12:00:00Z\n" +
                "D\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(root)) + "\n" +
                "K\tcreated\tfile\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.Combine(root, "example.txt"))) + "\n");
            IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void EventsAppendedAfterSnapshotRemainPending()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-concurrent-events-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var journal = Path.Combine(temp, "events");
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.txt");
            File.WriteAllText(first, "first");
            File.WriteAllText(journal, Event(first));
            var result = IncrementalJournalPublisher.Publish(root, catalog, journal,
                afterJournalSnapshot: () =>
                {
                    File.WriteAllText(second, "second");
                    File.AppendAllText(journal, Event(second));
                });
            Assert.True(result.Changed);
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var firstCatalog = LegacyCatalogReader.ReadFile(catalog);
            Assert.Single(firstCatalog.EnumerateEntries());
            Assert.True(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var finalCatalog = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(finalCatalog.EnumerateEntries(),
                e => finalCatalog.GetFullPath(e.Id) == first);
            Assert.Contains(finalCatalog.EnumerateEntries(),
                e => finalCatalog.GetFullPath(e.Id) == second);
        }
        finally { Directory.Delete(temp, recursive: true); }

        static string Event(string path) => "E" + (char)9 + "created" + (char)9 +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(path)) + (char)10;
    }

    [Fact]
    public void CompletePrefixIsPublishedWhileTrailingRecordRemainsPending()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-journal-prefix-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var journal = Path.Combine(temp, "events");
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.txt");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");
            static string Event(string path) => "E" + (char)9 + "created" + (char)9 +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(path)) + (char)10;
            var firstRecord = Event(first);
            var secondRecord = Event(second);
            File.WriteAllText(journal, firstRecord + secondRecord[..^1]);
            Assert.Equal(Encoding.UTF8.GetByteCount(firstRecord),
                IncrementalJournalPublisher.SnapshotLength(journal));
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var result = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.True(result.Changed);
            Assert.Equal(Encoding.UTF8.GetByteCount(firstRecord), result.AcknowledgedBytes);
            var published = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(published.EnumerateEntries(), e => published.GetFullPath(e.Id) == first);
            Assert.DoesNotContain(published.EnumerateEntries(), e => published.GetFullPath(e.Id) == second);
            File.AppendAllText(journal, Environment.NewLine);
            Assert.True(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            published = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(published.EnumerateEntries(), e => published.GetFullPath(e.Id) == second);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ExternalCatalogPublicationDiscardsOldLocalHintsWithoutOverwritingForeignChanges()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-foreign-publication-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "shared.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var journal = Path.Combine(temp, "host-a.events");
            var local = Path.Combine(root, "local.txt");
            File.WriteAllText(local, "local");
            File.WriteAllText(journal, "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(local)) + "\n");
            Assert.True(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            var cursor = IncrementalCursorPath.For(catalog, journal);
            var acknowledged = File.ReadAllBytes(cursor);

            // Host B changes the shared catalog while host A has its own cursor.
            var foreign = Path.Combine(root, "foreign.txt");
            File.WriteAllText(foreign, "foreign");
            LegacyCatalogWriter.Build(root, catalog);
            File.SetLastWriteTimeUtc(catalog, DateTime.UtcNow.AddSeconds(5));

            // Host A replays its own historical hints against the current catalog.
            // It must preserve entries published by host B.
            File.AppendAllText(journal, "E\tchanged\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(local)) + "\n");
            var result = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.False(result.Changed);
            Assert.NotEqual(acknowledged, File.ReadAllBytes(cursor));
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var current = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(current.EnumerateEntries(),
                e => current.GetFullPath(e.Id) == foreign);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ChangedCatalogLengthInvalidatesCursorEvenWithUnchangedTimestamp()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-same-timestamp-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "shared.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var journal = Path.Combine(temp, "host-a.events");
            var local = Path.Combine(root, "local.txt");
            File.WriteAllText(local, "local");
            File.WriteAllText(journal, "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(local)) + "\n");
            Assert.True(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            var originalTime = File.GetLastWriteTimeUtc(catalog);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);

            var foreign = Path.Combine(root, "foreign-file-with-a-long-distinct-name.txt");
            File.WriteAllText(foreign, new string('x', 10000));
            LegacyCatalogWriter.Build(root, catalog);
            File.SetLastWriteTimeUtc(catalog, originalTime);
            Assert.Equal(originalTime.Ticks, File.GetLastWriteTimeUtc(catalog).Ticks);

            // A timestamp-only cursor would incorrectly report no pending events.
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var reset = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.False(reset.Changed);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var current = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(current.EnumerateEntries(),
                e => current.GetFullPath(e.Id) == foreign);
        }
        finally { Directory.Delete(temp, true); }
    }

}
