using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalHistoryTests
{
    [Fact]
    public async Task RecordsIncrementalSuccessAndFailureWithoutSchedulingFullScans()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var catalog = Path.Combine(temp, "catalog.txt");
            File.WriteAllText(catalog, "existing");
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(catalog, now.UtcDateTime);
            var full = 0;
            var attempts = 0;
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(temp, catalog, "History", [], TimeSpan.FromDays(7),
                    IncrementalInterval: TimeSpan.FromMinutes(15))],
                execute: (_, _) => { full++; return Task.CompletedTask; },
                incremental: (_, _) =>
                {
                    attempts++;
                    if (attempts == 2) throw new IOException("share interrupted");
                    return Task.FromResult(true);
                },
                incrementalMetrics: _ => (8, 3, 2));

            await scheduler.CheckOnceAsync(now);
            await scheduler.CheckOnceAsync(now.AddMinutes(16));
            var runs = scheduler.History;
            Assert.Equal(2, runs.Length);
            Assert.Equal("incremental", runs[0].Kind);
            Assert.Equal("failed", runs[0].State);
            Assert.Contains("share interrupted", runs[0].Error);
            Assert.Equal("incremental", runs[1].Kind);
            Assert.Equal("completed", runs[1].State);
            Assert.Equal(8, runs[1].EventRecords);
            Assert.Equal(3, runs[1].DistinctPaths);
            Assert.Equal(2, runs[1].ProcessedPaths);
            Assert.Equal(0, full);
        }
        finally { Directory.Delete(temp, true); }
    }
}
