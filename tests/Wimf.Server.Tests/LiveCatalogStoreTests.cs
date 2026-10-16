using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class LiveCatalogStoreTests
{
    private static string Fixture(string name) =>
        $"[WIMF Catalog]\n{name}\nC:\\Fixture\n0\ntxt\n{name}.txt;txt;1;0;2\n";

    [Fact]
    public async Task NewCatalogBecomesVisibleWithoutRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            File.WriteAllText(source, Fixture("First"), Encoding.UTF8);
            var store = new LiveCatalogStore([], [source], Path.Combine(root, "cache"));
            var initial = await store.RefreshAsync();
            Assert.Equal(1, initial.CatalogCount);
            var oldSnapshot = store.Snapshot;
            Assert.Equal("First", oldSnapshot[0].Header.Name);
            File.WriteAllText(source, Fixture("Second"), Encoding.UTF8);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(2));
            var updated = await store.RefreshAsync();
            Assert.Equal(0, updated.FailedSources);
            Assert.Equal("Second", store.Snapshot[0].Header.Name);
            Assert.Equal("First", oldSnapshot[0].Header.Name);
            Assert.NotSame(oldSnapshot, store.Snapshot);
            Assert.NotNull(store.LastRefresh);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnavailableSourceRetainsExistingSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            File.WriteAllText(source, Fixture("First"), Encoding.UTF8);
            var store = new LiveCatalogStore([], [source], Path.Combine(root, "cache"));
            await store.RefreshAsync();
            File.Delete(source);
            var result = await store.RefreshAsync();
            Assert.Equal(1, result.FailedSources);
            Assert.Equal("First", store.Snapshot[0].Header.Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancellationDoesNotPublishPartialSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            File.WriteAllText(source, Fixture("First"), Encoding.UTF8);
            var store = new LiveCatalogStore([], [source], Path.Combine(root, "cache"));
            await store.RefreshAsync();
            var before = store.Snapshot;
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RefreshAsync(cts.Token));
            Assert.Same(before, store.Snapshot);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task UnchangedSourceReusesCatalogAndSnapshotWithoutParsingAgain()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-noop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            var cache = Path.Combine(root, "cache");
            File.WriteAllText(source, Fixture("Stable"), Encoding.UTF8);
            var store = new LiveCatalogStore([], [source], cache);
            await store.RefreshAsync();
            var snapshot = store.Snapshot;
            var catalog = snapshot[0];
            var cachePath = CatalogCache.CachePath(source, cache);
            var timestamp = File.GetLastWriteTimeUtc(cachePath);
            for (var i = 0; i < 3; i++)
            {
                var result = await store.RefreshAsync();
                Assert.Equal(0, result.FailedSources);
                Assert.Same(snapshot, store.Snapshot);
                Assert.Same(catalog, store.Snapshot[0]);
                Assert.Equal(timestamp, File.GetLastWriteTimeUtc(cachePath));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingSourceKeepsInMemoryCatalogEvenIfCacheRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            var cache = Path.Combine(root, "cache");
            File.WriteAllText(source, Fixture("Stable"), Encoding.UTF8);
            var store = new LiveCatalogStore([], [source], cache);
            await store.RefreshAsync();
            var snapshot = store.Snapshot;
            File.Delete(source);
            File.Delete(CatalogCache.CachePath(source, cache));
            var result = await store.RefreshAsync();
            Assert.Equal(1, result.FailedSources);
            Assert.Same(snapshot, store.Snapshot);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ReplacesChangedLocalCatalogWithoutRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "local.txt.gz");
            WriteGzipCatalog(path, Fixture("First"));
            var store = new LiveCatalogStore([path], Array.Empty<string>(), null, loadLocalFiles: true);
            var original = store.Snapshot;
            Assert.Equal("First", original[0].Header.Name);

            WriteGzipCatalog(path, Fixture("Second"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(3));
            var result = await store.RefreshAsync();
            Assert.Equal(0, result.FailedLocalCatalogs);
            Assert.Equal("Second", store.Snapshot[0].Header.Name);
            Assert.Equal("First", original[0].Header.Name);
            Assert.NotSame(original, store.Snapshot);

            var unchanged = store.Snapshot;
            await store.RefreshAsync();
            Assert.Same(unchanged, store.Snapshot);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task BrokenOrMissingLocalFileKeepsLastGoodCatalogUntilRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "local.txt");
            File.WriteAllText(path, Fixture("Original"));
            var store = new LiveCatalogStore([path], Array.Empty<string>(), null);
            var previous = store.Snapshot;
            File.WriteAllText(path, "not a WIMF catalog");
            var invalid = await store.RefreshAsync();
            Assert.Equal(1, invalid.FailedLocalCatalogs);
            Assert.Same(previous, store.Snapshot);
            File.Delete(path);
            var missing = await store.RefreshAsync();
            Assert.Equal(1, missing.FailedLocalCatalogs);
            Assert.Same(previous, store.Snapshot);
            File.WriteAllText(path, Fixture("Recovered"));
            var repaired = await store.RefreshAsync();
            Assert.Equal(0, repaired.FailedLocalCatalogs);
            Assert.Equal("Recovered", store.Snapshot[0].Header.Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ChangedLocalCatalogRetainsPositionBeforeNetworkCatalogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "local.txt");
            var network = Path.Combine(root, "network.txt");
            File.WriteAllText(path, Fixture("Local"));
            File.WriteAllText(network, Fixture("Network"));
            var store = new LiveCatalogStore([path], [network], Path.Combine(root, "cache"), loadLocalFiles: true);
            await store.RefreshAsync();
            Assert.Equal(["Local", "Network"], store.Snapshot.Select(x => x.Header.Name));
            File.WriteAllText(path, Fixture("UpdatedLocal"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(3));
            await store.RefreshAsync();
            Assert.Equal(["UpdatedLocal", "Network"], store.Snapshot.Select(x => x.Header.Name));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void WriteGzipCatalog(string path, string text)
    {
        using var file = File.Create(path);
        using var gzip = new System.IO.Compression.GZipStream(file,
            System.IO.Compression.CompressionLevel.Fastest);
        using var writer = new StreamWriter(gzip, Encoding.UTF8);
        writer.Write(text);
    }

}
