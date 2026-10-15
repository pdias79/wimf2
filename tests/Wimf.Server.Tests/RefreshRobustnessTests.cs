using System.Text;
using Wimf.Catalog;
using Wimf.Search;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

/// <summary>Stress and failure coverage for atomic publication of live catalog snapshots.</summary>
public sealed class RefreshRobustnessTests
{
    private static string Fixture(string version, int files = 4)
    {
        var lines = Enumerable.Range(1, files)
            .Select(id => $"{version}-{id}.txt;txt;{id};0;2");
        return "[WIMF Catalog]\n" + version + "\nC:\\Fixture\n0\ntxt\n" +
            string.Join("\n", lines) + "\n";
    }

    private static void WriteVersion(string path, string version, int files = 4)
    {
        // Different versions must have different metadata for the source cache fast path.
        // Publishing by rename prevents reading a partially written source.
        var temporary = path + ".next";
        File.WriteAllText(temporary, Fixture(version, files), Encoding.UTF8);
        File.SetLastWriteTimeUtc(temporary, DateTime.UtcNow.AddMinutes(10));
        File.Move(temporary, path, overwrite: true);
    }

    private static string Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-robust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task ConcurrentRefreshesAndSearchesObserveOnlyCompleteSnapshots()
    {
        var root = Root();
        try
        {
            var source = Path.Combine(root, "source.txt");
            WriteVersion(source, "Old", files: 128);
            var store = new LiveCatalogStore([], [source], Path.Combine(root, "cache"));
            await store.RefreshAsync();
            var old = store.Snapshot;
            Assert.Equal(128, old[0].RecordCount);

            WriteVersion(source, "New", files: 256);
            var engine = new SequentialSearchEngine();
            using var start = new ManualResetEventSlim(false);

            var searches = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                start.Wait();
                for (var i = 0; i < 300; i++)
                {
                    var snapshot = store.Snapshot;
                    Assert.Single(snapshot);
                    var catalog = snapshot[0];
                    var version = catalog.Header.Name;
                    Assert.True(version is "Old" or "New");
                    Assert.Equal(version == "Old" ? 128 : 256, catalog.RecordCount);
                    var found = engine.Search(snapshot, new SearchQuery
                    {
                        Text = version + "-",
                        MaxResults = 1000
                    });
                    Assert.Equal(catalog.RecordCount, found.Count);
                    Assert.All(found, hit =>
                    {
                        Assert.Same(catalog, hit.Catalog);
                        Assert.StartsWith(version + "-", hit.Entry.Name);
                        Assert.Contains(hit.Entry.Name, hit.FullPath);
                    });
                }
            })).ToArray();

            var refreshes = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(async () =>
                {
                    start.Wait();
                    var result = await store.RefreshAsync();
                    Assert.Equal(0, result.FailedSources);
                    Assert.Equal(1, result.CatalogCount);
                })).ToArray();

            start.Set();
            await Task.WhenAll(searches.Concat(refreshes));
            Assert.Equal("New", store.Snapshot[0].Header.Name);
            Assert.Equal(256, store.Snapshot[0].RecordCount);
            Assert.Equal("Old", old[0].Header.Name);
            Assert.Equal(128, old[0].RecordCount);
            Assert.Equal(128, engine.Search(old, new SearchQuery { Text = "Old-", MaxResults = 1000 }).Count);
            Assert.Empty(engine.Search(old, new SearchQuery { Text = "New-" }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedSecondSourceKeepsPreviousCatalogWhileFirstSourceUpdates()
    {
        var root = Root();
        try
        {
            var first = Path.Combine(root, "a.txt");
            var second = Path.Combine(root, "b.txt");
            WriteVersion(first, "A-old");
            WriteVersion(second, "B-old");
            var cache = Path.Combine(root, "cache");
            var store = new LiveCatalogStore([], [first, second], cache);
            await store.RefreshAsync();
            var old = store.Snapshot;
            WriteVersion(first, "A-new");
            File.WriteAllText(second, "invalid catalog signature", Encoding.UTF8);
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddMinutes(20));

            var refreshed = await store.RefreshAsync();
            Assert.Equal(2, refreshed.CatalogCount);
            Assert.Equal(2, refreshed.SourceCount);
            Assert.Equal(1, refreshed.FailedSources);
            Assert.NotSame(old, store.Snapshot);
            Assert.Equal("A-new", store.Snapshot[0].Header.Name);
            Assert.Same(old[1], store.Snapshot[1]);
            Assert.Equal("B-old", old[1].Header.Name);
            Assert.Equal("B-old", LegacyCatalogReader.ReadFile(CatalogCache.CachePath(second, cache)).Header.Name);
            Assert.Empty(Directory.GetFiles(cache, "*.tmp"));

            // Fixing the source recovers without restart or corrupting a prior snapshot.
            WriteVersion(second, "B-fixed");
            var recovered = await store.RefreshAsync();
            Assert.Equal(0, recovered.FailedSources);
            Assert.Equal("B-fixed", store.Snapshot[1].Header.Name);
            Assert.Equal("B-old", old[1].Header.Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidCacheOnColdStartIsNotPublished()
    {
        var root = Root();
        try
        {
            var source = Path.Combine(root, "missing.txt");
            var cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(cache);
            File.WriteAllText(CatalogCache.CachePath(source, cache),
                "[WIMF Catalog]\nmissing header", Encoding.UTF8);

            Assert.Throws<IOException>(() => CatalogCache.Load(source, cache));
        }
        finally { Directory.Delete(root, true); }
    }
}
