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
                {"catalogs":["c.txt.gz","d.txt.gz","m.txt.gz"],
                "indexing":[
                  {"root":"Cdrive","output":"c.txt.gz","name":"C", "intervalHours":6,"exclude":["Temp"]},
                  {"root":"Archive","output":"d.txt.gz","intervalDays":7,"exclude":["Private","Old"]},
                  {"root":"Quick","output":"m.txt.gz","intervalMinutes":5,"exclude":[]}
                ]}
                """);
            var options = DaemonOptions.Parse(["--config", file]);
            Assert.NotNull(options.Indexing);
            Assert.Equal(3, options.Indexing!.Length);
            Assert.Equal(TimeSpan.FromHours(6), options.Indexing[0].Interval);
            Assert.Equal(TimeSpan.FromDays(7), options.Indexing[1].Interval);
            Assert.Equal(TimeSpan.FromMinutes(5), options.Indexing[2].Interval);
            Assert.Equal(Path.Combine(folder, "Cdrive"), options.Indexing[0].Root);
            Assert.Equal(Path.Combine(folder, "d.txt.gz"), options.Indexing[1].Output);
            Assert.Equal(["Private", "Old"], options.Indexing[1].Exclude);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz"}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalMinutes":0}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalMinutes":-5}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalMinutes":5256001}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalMinutes":5,"intervalHours":1}]}""")]
    [InlineData("""{"catalogs":["c.gz"],"indexing":[{"root":"a","output":"c.gz","intervalMinutes":5,"intervalDays":1}]}""")]
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
    [Fact]
    public async Task SchedulerRespectsMinuteInterval()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-minute-sched-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "catalog.txt");
            File.WriteAllText(path, "initial");
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(path, now.AddMinutes(-4).UtcDateTime);
            var executions = 0;
            var job = new IndexingJob(folder, path, null, [], TimeSpan.FromMinutes(5));
            var scheduler = new CatalogIndexScheduler([job], (j, ct) =>
            {
                executions++;
                File.SetLastWriteTimeUtc(j.Output, now.UtcDateTime);
                return Task.CompletedTask;
            });
            await scheduler.CheckOnceAsync(now);
            Assert.Equal(0, executions);
            await scheduler.CheckOnceAsync(now.AddMinutes(2));
            Assert.Equal(1, executions);
            await scheduler.CheckOnceAsync(now.AddMinutes(3));
            Assert.Equal(1, executions);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task OfflineNetworkRootIsReportedAndRetriedWhenItReturns()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "wimf-root-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var missingRoot = Path.Combine(workspace, "offline-share");
            var output = Path.Combine(workspace, "index.txt");
            var executions = 0;
            var job = new IndexingJob(missingRoot, output, "Offline share", [], TimeSpan.FromMinutes(5));
            var scheduler = new CatalogIndexScheduler([job], (j, ct) =>
            {
                executions++;
                File.WriteAllText(j.Output, "created");
                return Task.CompletedTask;
            });
            var now = DateTimeOffset.UtcNow;
            await scheduler.CheckOnceAsync(now);
            Assert.Equal(0, executions);
            Assert.Equal("failed", scheduler.Status[0].State);
            Assert.Contains("Indexing root unavailable", scheduler.Status[0].LastError);
            Directory.CreateDirectory(missingRoot);
            await scheduler.CheckOnceAsync(now.AddMinutes(2));
            Assert.Equal(1, executions);
            Assert.Equal("completed", scheduler.Status[0].State);
            Assert.Null(scheduler.Status[0].LastError);
        }
        finally { Directory.Delete(workspace, true); }
    }

}
