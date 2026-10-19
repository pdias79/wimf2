using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IndexingHistoryStoreTests
{
    [Fact]
    public void SaveAndLoadAcrossInstancesKeepsLastFifty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-history-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "history.json");
            var store = new IndexingHistoryStore(path);
            var now = DateTimeOffset.UtcNow;
            var entries = Enumerable.Range(0, 60).Select(i =>
                new IndexingRunHistoryEntry("Catalog-" + i, "completed", now, now,
                    0, 0, null, "incremental", i, 1, 1)).ToArray();
            store.Save(entries);
            var loaded = new IndexingHistoryStore(path).Load();
            Assert.Equal(50, loaded.Length);
            Assert.Equal("Catalog-0", loaded[0].Name);
            Assert.Equal("Catalog-49", loaded[^1].Name);
            Assert.Equal(5, loaded[5].EventRecords);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void CorruptFileIsIgnoredAndFutureSaveWorks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-history-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "history.json");
            File.WriteAllText(path, "{invalid json");
            var store = new IndexingHistoryStore(path);
            Assert.Empty(store.Load());
            var now = DateTimeOffset.UtcNow;
            store.Save([new IndexingRunHistoryEntry("Recovered", "completed", now, now, 4, 0, null)]);
            Assert.Single(store.Load());
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
