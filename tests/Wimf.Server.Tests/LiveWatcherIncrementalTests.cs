using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class LiveWatcherIncrementalTests
{
    [Fact]
    public async Task RealFileSystemWatcherPublishesCreatedAndDeletedFiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-watch-live-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        var state = Path.Combine(temp, "watch-state");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            ScheduledCatalogPublication.Refresh(root, catalog);
            using (var watcher = new SmbBackgroundWatcher(root, state))
            {
                watcher.Start();
                // Ack the startup marker before creating any files.
                IncrementalJournalPublisher.AcknowledgeFullScan(catalog, watcher.JournalPath,
                    IncrementalJournalPublisher.SnapshotLength(watcher.JournalPath));
                var added = Path.Combine(root, "live.txt");
                File.WriteAllText(added, "live watcher test");
                await WaitForPendingAsync(catalog, watcher.JournalPath);
                Assert.True(IncrementalJournalPublisher.Publish(root, catalog, watcher.JournalPath).Changed);
                var indexed = LegacyCatalogReader.ReadFile(catalog);
                Assert.Contains(indexed.EnumerateEntries(), e => indexed.GetFullPath(e.Id) == added);

                File.Delete(added);
                await WaitForPendingAsync(catalog, watcher.JournalPath);
                Assert.True(IncrementalJournalPublisher.Publish(root, catalog, watcher.JournalPath).Changed);
                indexed = LegacyCatalogReader.ReadFile(catalog);
                Assert.DoesNotContain(indexed.EnumerateEntries(), e => indexed.GetFullPath(e.Id) == added);
            }
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static async Task WaitForPendingAsync(string catalog, string journal)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            if (IncrementalJournalPublisher.Pending(catalog, journal).Events > 0) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("FileSystemWatcher did not journal the expected filesystem event.");
    }
}
