using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class ReconciliationSchedulerTests
{
    [Fact]
    public async Task LostWatcherCoverageTriggersFullScanBeforeNormalDeadline()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-reconcile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "catalog.txt");
            File.WriteAllText(output, "existing");
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(output, now.UtcDateTime);
            var required = true;
            var full = 0;
            var incremental = 0;
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(root, output, "Test", [], TimeSpan.FromDays(7),
                    IncrementalInterval: TimeSpan.FromMinutes(1))],
                execute: (_, _) => { full++; return Task.CompletedTask; },
                afterFull: (_, _) => required = false,
                reconciliationRequired: _ => required,
                incremental: (_, _) => { incremental++; return Task.FromResult(false); });

            await scheduler.CheckOnceAsync(now);
            Assert.Equal(1, full);
            Assert.Equal(0, incremental);
            Assert.False(required);

            await scheduler.CheckOnceAsync(now.AddSeconds(10));
            Assert.Equal(1, full);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedReconciliationRetainsRequestAndBacksOff()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-reconcile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "catalog.txt");
            File.WriteAllText(output, "existing");
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(output, now.UtcDateTime);
            var full = 0;
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(root, output, "Test", [], TimeSpan.FromDays(7))],
                execute: (_, _) => { full++; throw new IOException("SMB temporarily inaccessible"); },
                reconciliationRequired: _ => true);
            await scheduler.CheckOnceAsync(now);
            await scheduler.CheckOnceAsync(now.AddMinutes(1));
            Assert.Equal(1, full);
            Assert.Equal("failed", scheduler.Status[0].State);
            await scheduler.CheckOnceAsync(now.AddMinutes(6));
            Assert.Equal(2, full);
        }
        finally { Directory.Delete(root, true); }
    }
}
