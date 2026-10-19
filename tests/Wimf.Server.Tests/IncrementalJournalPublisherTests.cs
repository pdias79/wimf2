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
            var result = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.True(result.Changed);
            Assert.Equal(1, LegacyCatalogReader.ReadFile(catalog).EnumerateEntries().Count());
            Assert.False(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            var second = Path.Combine(root, "second.txt");
            File.WriteAllText(second, "2");
            File.AppendAllText(journal, "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(second)) + "\n");
            Assert.True(IncrementalJournalPublisher.Publish(root, catalog, journal).Changed);
            Assert.Equal(2, LegacyCatalogReader.ReadFile(catalog).EnumerateEntries().Count());
        }
        finally { Directory.Delete(temp, true); }
    }
}
