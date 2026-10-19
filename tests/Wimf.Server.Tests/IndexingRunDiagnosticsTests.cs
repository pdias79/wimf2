using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IndexingRunDiagnosticsTests
{
    [Fact]
    public void DetectsSlowLatestSuccessfulRun()
    {
        var now = DateTimeOffset.UtcNow;
        IndexingRunHistoryEntry Entry(int seconds, string state) =>
            new("Catalog", state, now, now.AddSeconds(seconds), 0, 0, null, "incremental");
        var result = IndexingRunDiagnostics.Calculate([
            Entry(12, "completed"), Entry(4, "completed"), Entry(2, "completed"),
            Entry(10, "failed")]);
        Assert.True(result.UnusuallySlow);
        Assert.Equal(75, result.SuccessRatePercent);
        Assert.Equal(12, result.LatestSeconds);
        Assert.Equal(6, result.AverageSeconds);
    }

    [Fact]
    public void DoesNotWarnWithoutHistoricalBaseline()
    {
        var now = DateTimeOffset.UtcNow;
        var result = IndexingRunDiagnostics.Calculate([
            new IndexingRunHistoryEntry("Only", "completed", now, now.AddSeconds(100), 0, 0, null)]);
        Assert.False(result.UnusuallySlow);
        Assert.Equal(100, result.SuccessRatePercent);
        Assert.Null(IndexingRunDiagnostics.Calculate([]).LatestSeconds);
    }
}
