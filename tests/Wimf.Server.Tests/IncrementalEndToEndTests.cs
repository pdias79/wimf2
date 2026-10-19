using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalEndToEndTests
{
    [Fact]
    public async Task ScheduledFullThenIncrementalUpdatesOnlyChangedPaths()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-incremental-e2e-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            var journal = Path.Combine(temp, "events");
            File.WriteAllText(journal, "");
            File.WriteAllText(Path.Combine(root, "initial.txt"), "initial");
            var job = new IndexingJob(root, catalog, "Integration", [], TimeSpan.FromDays(7),
                IncrementalInterval: TimeSpan.FromMinutes(1));
            var fullScans = 0;
            var scheduler = new CatalogIndexScheduler([job],
                execute: (_, _) =>
                {
                    fullScans++;
                    ScheduledCatalogPublication.Refresh(root, catalog);
                    return Task.CompletedTask;
                },
                beforeFull: _ => IncrementalJournalPublisher.SnapshotLength(journal),
                afterFull: (_, boundary) =>
                    IncrementalJournalPublisher.AcknowledgeFullScan(catalog, journal, boundary),
                incremental: (_, token) =>
                    Task.FromResult(IncrementalJournalPublisher.Publish(root, catalog, journal, token).Changed));
            var now = DateTimeOffset.UtcNow;
            await scheduler.CheckOnceAsync(now);
            Assert.Equal(1, fullScans);
            var created = Path.Combine(root, "added.txt");
            File.WriteAllText(created, "added");
            File.AppendAllText(journal, "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(created)) + "\n");
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            await scheduler.CheckOnceAsync(now.AddMinutes(2));
            Assert.Equal(1, fullScans);
            Assert.Equal(0, IncrementalJournalPublisher.Pending(catalog, journal).Events);
            var published = LegacyCatalogReader.ReadFile(catalog);
            var names = published.EnumerateEntries().Select(e => Path.GetFileName(published.GetFullPath(e.Id))).ToArray();
            Assert.Contains("initial.txt", names);
            Assert.Contains("added.txt", names);
            File.Delete(created);
            File.AppendAllText(journal, "E\tdeleted\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(created)) + "\n");
            await scheduler.CheckOnceAsync(now.AddMinutes(4));
            published = LegacyCatalogReader.ReadFile(catalog);
            names = published.EnumerateEntries().Select(e => Path.GetFileName(published.GetFullPath(e.Id))).ToArray();
            Assert.DoesNotContain("added.txt", names);
            Assert.Contains("initial.txt", names);
            Assert.Equal(1, fullScans);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
