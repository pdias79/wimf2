using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IndexingPerformanceSummaryTests
{
    [Fact]
    public void AggregatesPerCatalogAndScanKindAndExcludesFailuresFromMean()
    {
        var now = DateTimeOffset.UtcNow;
        IndexingRunHistoryEntry Run(string name, string kind, int seconds, string state, int events) =>
            new(name, state, now, now.AddSeconds(seconds), 0, 0, null, kind, events);
        var summary = IndexingPerformanceSummary.Calculate([
            Run("A", "incremental", 2, "completed", 10),
            Run("A", "incremental", 6, "completed", 20),
            Run("A", "incremental", 20, "failed", 0),
            Run("A", "full", 50, "completed", 0),
            Run("B", "incremental", 4, "completed", 3)
        ]);
        Assert.Equal(3, summary.Length);
        var incremental = Assert.Single(summary, g => g.Name == "A" && g.Kind == "incremental");
        Assert.Equal(3, incremental.Runs);
        Assert.Equal(1, incremental.Failures);
        Assert.Equal(4, incremental.AverageSeconds);
        Assert.Equal(30, incremental.TotalEvents);
        var full = Assert.Single(summary, g => g.Name == "A" && g.Kind == "full");
        Assert.Equal(50, full.AverageSeconds);
        Assert.Equal(0, full.TotalEvents);
    }
}
