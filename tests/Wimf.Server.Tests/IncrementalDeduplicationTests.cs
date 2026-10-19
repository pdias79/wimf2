using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalDeduplicationTests
{
    [Fact]
    public void RepeatedChildrenUnderNotifiedParentAreScannedOnce()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-dedup-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var first = Path.Combine(sub, "a.txt");
            var second = Path.Combine(sub, "b.txt");
            File.WriteAllText(first, "a");
            File.WriteAllText(second, "b");
            var journal = Path.Combine(temp, "journal.events");
            static string Event(string path) => "E\tchanged\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(path)) + "\n";
            File.WriteAllText(journal, Event(sub) + Event(first) + Event(first) + Event(second));
            var result = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.True(result.Changed);
            Assert.Equal(4, result.EventRecords);
            Assert.Equal(3, result.DistinctPaths);
            Assert.Equal(1, result.Hints);
            Assert.True(result.ElapsedMilliseconds >= 0);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var published = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(published.EnumerateEntries(), e => published.GetFullPath(e.Id) == first);
            Assert.Contains(published.EnumerateEntries(), e => published.GetFullPath(e.Id) == second);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void DeletingNotifiedParentRemovesItsDescendants()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-dedup-delete-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            var child = Path.Combine(sub, "old.txt");
            File.WriteAllText(child, "old");
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            Directory.Delete(sub, recursive: true);
            var journal = Path.Combine(temp, "journal.events");
            static string Event(string path) => "E\tdeleted\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(path)) + "\n";
            File.WriteAllText(journal, Event(child) + Event(sub));
            var result = IncrementalJournalPublisher.Publish(root, catalog, journal);
            Assert.True(result.Changed);
            Assert.Equal(1, result.Hints);
            var published = LegacyCatalogReader.ReadFile(catalog);
            Assert.DoesNotContain(published.EnumerateEntries(), e => published.GetFullPath(e.Id) == child);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
        }
        finally { Directory.Delete(temp, true); }
    }
}
