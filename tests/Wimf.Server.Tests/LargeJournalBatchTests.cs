using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class LargeJournalBatchTests
{
    [Fact]
    public void LargeJournalProcessesMultipleBatchesWithoutDroppingPendingEvents()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-journal-batch-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var first = Path.Combine(root, "first.txt");
            var last = Path.Combine(root, "last.txt");
            File.WriteAllText(first, "first");
            File.WriteAllText(last, "last");
            var journal = Path.Combine(temp, "journal.events");
            static string Event(string path) =>
                "E\tcreated\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(path)) + "\n";
            using (var writer = new StreamWriter(journal, append: false, new UTF8Encoding(false)))
            {
                writer.Write(Event(first));
                var padding = "S\tstart\t2026-10-19T00:00:00.0000000+00:00\n";
                for (var i = 0; i < 240000; i++) writer.Write(padding);
                writer.Write(Event(last));
            }
            Assert.True(new FileInfo(journal).Length > 8 * 1024 * 1024);
            Assert.Equal(2, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var firstBatch = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.True(firstBatch.Changed);
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var firstCatalog = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(firstCatalog.EnumerateEntries(),
                e => firstCatalog.GetFullPath(e.Id) == first);
            var secondBatch = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.True(secondBatch.Changed);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var final = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(final.EnumerateEntries(), e => final.GetFullPath(e.Id) == first);
            Assert.Contains(final.EnumerateEntries(), e => final.GetFullPath(e.Id) == last);
        }
        finally { Directory.Delete(temp, true); }
    }
}
