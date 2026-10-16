using System.Text.Json;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class ScheduledIndexingTests
{
    [Fact]
    public void ParsesIndependentHourlyAndMultiDayJobs()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-index-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "wimf.json");
            File.WriteAllText(file, """
                {"catalogs":["c.txt.gz","d.txt.gz"],
                "indexing":[
                  {"root":"Cdrive","output":"c.txt.gz","name":"C", "intervalHours":6,"exclude":["Temp"]},
                  {"root":"Archive","output":"d.txt.gz","intervalDays":7,"exclude":["Private","Old"]}
                ]}
                """);
            var options = DaemonOptions.Parse(["--config", file]);
            Assert.NotNull(options.Indexing);
            Assert.Equal(2, options.Indexing!.Length);
            Assert.Equal(TimeSpan.FromHours(6), options.Indexing[0].Interval);
            Assert.Equal(TimeSpan.FromDays(7), options.Indexing[1].Interval);
            Assert.Equal(Path.Combine(folder, "Cdrive"), options.Indexing[0].Root);
            Assert.Equal(Path.Combine(folder, "d.txt.gz"), options.Indexing[1].Output);
            Assert.Equal(["Private", "Old"], options.Indexing[1].Exclude);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz"}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalHours":0}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalDays":-1}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalHours":1,"intervalDays":2}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"other.gz","intervalDays":1}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalDays":1,"exclude":[".."]}]}""")]
    public void InvalidIndexingConfigurationIsRejected(string json)
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-index-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "wimf.json");
            File.WriteAllText(path, json);
            Assert.ThrowsAny<ArgumentException>(() => DaemonOptions.Parse(["--config", path]));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task SchedulerRespectsMultiDayIntervalAndTriggersOverdueJobs()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-index-sched-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "catalog.txt");
            File.WriteAllText(path, "dummy");
            var now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
            File.SetLastWriteTimeUtc(path, now.AddDays(-6).UtcDateTime);
            var count = 0;
            var job = new IndexingJob(folder, path, null, [], TimeSpan.FromDays(7));
            var scheduler = new CatalogIndexScheduler([job], (j, ct) =>
            {
                count++;
                File.SetLastWriteTimeUtc(j.Output, now.UtcDateTime);
                return Task.CompletedTask;
            });
            await scheduler.CheckOnceAsync(now);
            Assert.Equal(0, count);
            await scheduler.CheckOnceAsync(now.AddDays(2));
            Assert.Equal(1, count);
            await scheduler.CheckOnceAsync(now.AddDays(3));
            Assert.Equal(1, count);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task SchedulerDoesNotOverlapAndBuildsMissingCatalog()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-index-sched-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "missing.txt");
            var executions = 0;
            var job = new IndexingJob(folder, output, "Synthetic", [], TimeSpan.FromDays(3));
            var scheduler = new CatalogIndexScheduler([job], (j, ct) =>
            {
                executions++;
                File.WriteAllText(j.Output, "catalog built");
                return Task.CompletedTask;
            });
            var now = DateTimeOffset.UtcNow;
            await scheduler.CheckOnceAsync(now);
            await scheduler.CheckOnceAsync(now);
            Assert.Equal(1, executions);
        }
        finally { Directory.Delete(folder, true); }
    }
}
