using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class AutomaticIncrementalSchedulingTests
{
    [Fact]
    public async Task WatcherEventsAreProcessedOnIncrementalCadenceWithoutAnotherFullScan()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-auto-delta-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            var state = Path.Combine(temp, "watcher");
            var fullCount = 0;
            var incrementalCount = 0;
            using var watcher = new SmbBackgroundWatcher(root, state);
            watcher.Start();
            var job = new IndexingJob(root, catalog, "Automatic", [], TimeSpan.FromDays(7),
                IncrementalInterval: TimeSpan.FromMinutes(1));
            var scheduler = new CatalogIndexScheduler([job],
                execute: (_, token) =>
                {
                    fullCount++;
                    ScheduledCatalogPublication.Refresh(root, catalog, cancellationToken: token);
                    return Task.CompletedTask;
                },
                beforeFull: _ => IncrementalJournalPublisher.SnapshotLength(watcher.JournalPath),
                afterFull: (_, offset) =>
                    IncrementalJournalPublisher.AcknowledgeFullScan(catalog, watcher.JournalPath, offset),
                incremental: (_, token) =>
                {
                    incrementalCount++;
                    return Task.FromResult(IncrementalJournalPublisher.Publish(
                        root, catalog, watcher.JournalPath, token).Changed);
                });
            var start = DateTimeOffset.UtcNow;
            await scheduler.CheckOnceAsync(start);
            Assert.Equal(1, fullCount);

            var created = Path.Combine(root, "automatic.txt");
            File.WriteAllText(created, "test");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (IncrementalJournalPublisher.Pending(catalog, watcher.JournalPath).Events == 0)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(50, timeout.Token);
            }

            await scheduler.CheckOnceAsync(start.AddMinutes(2));
            Assert.Equal(1, fullCount);
            Assert.Equal(1, incrementalCount);
            // FileSystemWatcher can report additional Changed events after publication.
            // Those must remain pending for the next incremental pass, not be dropped.
            var published = LegacyCatalogReader.ReadFile(catalog);
            Assert.Contains(published.EnumerateEntries(), e => published.GetFullPath(e.Id) == created);

            await scheduler.CheckOnceAsync(start.AddMinutes(2).AddSeconds(20));
            Assert.Equal(1, incrementalCount);
            Assert.Equal(1, fullCount);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
