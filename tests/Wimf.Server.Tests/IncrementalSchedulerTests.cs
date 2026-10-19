using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalSchedulerTests
{
    [Fact]
    public async Task IncrementalCadenceDoesNotTriggerFullScanOrDelayPeriodicFullScan()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-scheduler-delta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = Path.Combine(directory, "catalog.txt");
            File.WriteAllText(catalog, "existing");
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(catalog, now.UtcDateTime);
            var full = 0;
            var incremental = 0;
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(directory, catalog, "Test", [], TimeSpan.FromDays(7),
                    IncrementalInterval: TimeSpan.FromMinutes(15))],
                execute: (_, _) => { full++; return Task.CompletedTask; },
                incremental: (_, _) => { incremental++; return Task.FromResult(false); });
            await scheduler.CheckOnceAsync(now);
            await scheduler.CheckOnceAsync(now.AddMinutes(5));
            await scheduler.CheckOnceAsync(now.AddMinutes(16));
            Assert.Equal(0, full);
            Assert.Equal(2, incremental);
        }
        finally { Directory.Delete(directory, true); }
    }
}
