using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalMonitoringTests
{
    [Fact]
    public async Task ExposingNextIncrementalAttemptDoesNotTriggerExtraFullScans()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-monitor-" + Guid.NewGuid().ToString("N"));
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
                    IncrementalInterval: TimeSpan.FromMinutes(15))],
                execute: (_, _) => { full++; return Task.CompletedTask; },
                incremental: (_, _) => { incremental++; return Task.FromResult(false); });

            await scheduler.CheckOnceAsync(now);
            var next = scheduler.Status[0].NextIncremental;
            Assert.NotNull(next);
            Assert.InRange(next!.Value, now.AddMinutes(14), now.AddMinutes(16));
            _ = scheduler.Status[0].NextIncremental;
            _ = scheduler.Status[0].NextIncremental;
            Assert.Equal(0, full);
            Assert.Equal(1, incremental);
        }
        finally { Directory.Delete(root, true); }
    }
}
