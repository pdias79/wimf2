using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class ReconciliationSchedulerTests
{
    [Fact]
    public async Task FullScanDeadlineIsNotAdvancedByIncrementalAttempts()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-cadence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "catalog.txt");
            File.WriteAllText(output, "existing");
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(output, now.UtcDateTime);
            var full = 0;
            var incremental = 0;
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(root, output, "Test", [], TimeSpan.FromDays(7),
                    IncrementalInterval: TimeSpan.FromMinutes(1))],
                execute: (_, _) => { full++; return Task.CompletedTask; },
                incremental: (_, _) => { incremental++; return Task.FromResult(false); });

            await scheduler.CheckOnceAsync(now.AddDays(1));
            await scheduler.CheckOnceAsync(now.AddDays(2));
            await scheduler.CheckOnceAsync(now.AddDays(6));
            Assert.Equal(0, full);
            Assert.Equal(3, incremental);

            await scheduler.CheckOnceAsync(now.AddDays(8));
            Assert.Equal(1, full);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task IncrementalFailureDoesNotForceEarlyFullScan()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-cadence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "catalog.txt");
            File.WriteAllText(output, "existing");
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(output, now.UtcDateTime);
            var full = 0;
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(root, output, "Test", [], TimeSpan.FromDays(7),
                    IncrementalInterval: TimeSpan.FromMinutes(1))],
                execute: (_, _) => { full++; return Task.CompletedTask; },
                incremental: (_, _) => throw new IOException("Watcher unavailable"));

            await scheduler.CheckOnceAsync(now.AddDays(1));
            await scheduler.CheckOnceAsync(now.AddDays(2));
            Assert.Equal(0, full);
            await scheduler.CheckOnceAsync(now.AddDays(8));
            Assert.Equal(1, full);
        }
        finally { Directory.Delete(root, true); }
    }
}
