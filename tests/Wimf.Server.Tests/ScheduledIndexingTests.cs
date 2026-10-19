using System.Text.Json;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class ScheduledIndexingTests
{
    [Fact]
    public async Task RunningScanCanBeCancelledAndRecorded()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var output = Path.Combine(root, "index.txt");
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(root, output, "Cancel test", [], TimeSpan.FromDays(1))],
                async (_, token) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                });
            var running = scheduler.CheckOnceAsync(DateTimeOffset.UtcNow);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(scheduler.TryCancel(0));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.Equal("cancelled", scheduler.Status[0].State);
            Assert.Single(scheduler.History);
            Assert.Equal("cancelled", scheduler.History[0].State);
            Assert.False(scheduler.TryCancel(0));
        }
        finally { Directory.Delete(root, true); }
    }

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

    [Fact]
    public async Task ManualIndexingStartsImmediatelyAndRejectsDuplicateRequests()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "catalog.txt");
            File.WriteAllText(output, "existing");
            File.SetLastWriteTimeUtc(output, DateTime.UtcNow);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(folder, output, "Manual", [], TimeSpan.FromDays(30))],
                async (job, token) =>
                {
                    Interlocked.Increment(ref count);
                    started.TrySetResult();
                    await resume.Task.WaitAsync(token);
                    File.WriteAllText(job.Output, "reindexed");
                });
            Assert.False(scheduler.TryQueueManual(-1, CancellationToken.None));
            Assert.False(scheduler.TryQueueManual(1, CancellationToken.None));
            Assert.True(scheduler.TryQueueManual(0, CancellationToken.None));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("running", scheduler.Status[0].State);
            Assert.False(scheduler.Status[0].CanStart);
            Assert.False(scheduler.TryQueueManual(0, CancellationToken.None));
            resume.TrySetResult();
            for (var attempt = 0; attempt < 50 && scheduler.Status[0].State != "completed"; attempt++)
                await Task.Delay(20);
            Assert.Equal("completed", scheduler.Status[0].State);
            Assert.Equal(1, count);
            Assert.True(scheduler.Status[0].CanStart);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ManualScanShowsQueuedWhileAnotherScanHoldsTheLock()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var first = Path.Combine(folder, "first.txt");
            var second = Path.Combine(folder, "second.txt");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(folder, first, "First", [], TimeSpan.FromDays(1)),
                 new IndexingJob(folder, second, "Second", [], TimeSpan.FromDays(1))],
                async (job, token) =>
                {
                    if (job.Output == first)
                    {
                        started.TrySetResult();
                        await release.Task.WaitAsync(token);
                    }
                    File.WriteAllText(job.Output, "done");
                });
            var scheduled = scheduler.CheckOnceAsync(DateTimeOffset.UtcNow);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(scheduler.TryQueueManual(1, CancellationToken.None));
            Assert.Equal("queued", scheduler.Status[1].State);
            Assert.NotNull(scheduler.Status[1].QueuedAt);
            Assert.False(scheduler.Status[1].CanStart);
            release.TrySetResult();
            await scheduled.WaitAsync(TimeSpan.FromSeconds(5));
            for (var attempt = 0; attempt < 100 && scheduler.Status[1].State != "completed"; attempt++)
                await Task.Delay(20);
            Assert.Equal("completed", scheduler.Status[1].State);
            Assert.Null(scheduler.Status[1].QueuedAt);
            // The public state may become completed before the queued background
            // task releases its cancellation source and file handles on Windows.
            for (var attempt = 0; attempt < 100 && !scheduler.Status[1].CanStart; attempt++)
                await Task.Delay(20);
            Assert.True(scheduler.Status[1].CanStart);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task SuccessfulScheduledScanImmediatelyReloadsCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-refresh-sched-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalogPath = Path.Combine(root, "catalog.txt");
            var catalogRoot = Path.Combine(root, "files");
            Directory.CreateDirectory(catalogRoot);
            var reloadCount = 0;
            var job = new IndexingJob(catalogRoot, catalogPath, "Files", [], TimeSpan.FromDays(1));
            var store = new LiveCatalogStore([catalogPath], [], null,
                loadLocalFiles: true, allowMissingCatalogs: [catalogPath]);
            Assert.Empty(store.Snapshot);
            var scheduler = new CatalogIndexScheduler([job],
                (j, ct) => Task.Run(() => Wimf.Catalog.LegacyCatalogWriter.Build(
                    j.Root, j.Output, j.Name, cancellationToken: ct), ct),
                async (j, ct) =>
                {
                    reloadCount++;
                    await store.RefreshAsync(ct);
                });
            await scheduler.CheckOnceAsync(DateTimeOffset.UtcNow);
            Assert.Equal(1, reloadCount);
            Assert.Single(store.Snapshot);
            Assert.Equal("Files", store.Snapshot[0].Header.Name);
            Assert.Equal("completed", scheduler.Status[0].State);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedReloadDoesNotTurnSuccessfulScanIntoFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-refresh-failed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "catalog.txt");
            var invoked = 0;
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(root, output, "Test", [], TimeSpan.FromDays(1))],
                (job, ct) =>
                {
                    File.WriteAllText(job.Output, "new catalog");
                    return Task.CompletedTask;
                },
                (job, ct) =>
                {
                    invoked++;
                    throw new IOException("Reload is unavailable");
                });
            await scheduler.CheckOnceAsync(DateTimeOffset.UtcNow);
            Assert.Equal(1, invoked);
            Assert.Equal("completed", scheduler.Status[0].State);
            Assert.Null(scheduler.Status[0].LastError);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task IndexingReportsScannedEntriesAndStartTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var files = Path.Combine(root, "files");
            Directory.CreateDirectory(files);
            for (var i = 0; i < 300; i++)
                File.WriteAllText(Path.Combine(files, $"item-{i:000}.txt"), "x");
            var catalog = Path.Combine(root, "catalog.txt.gz");
            var reports = new List<int>();
            var count = Wimf.Catalog.LegacyCatalogWriter.Build(
                files, catalog, "Progress", onProgress: reports.Add);
            Assert.Equal(300, count);
            Assert.Equal([256, 300], reports);

            // Exercise both creation of a missing catalog and refresh of an existing one.
            File.Delete(catalog);
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(files, catalog, "Progress", [], TimeSpan.FromMinutes(1))]);
            await scheduler.CheckOnceAsync(DateTimeOffset.UtcNow);
            Assert.Equal(300, Assert.Single(scheduler.Status).ScannedEntries);
            // Full-scan cadence now uses its own persisted timestamp, not catalog mtime.
            // Simulate the next scheduler tick after the configured one-minute interval.
            await scheduler.CheckOnceAsync(DateTimeOffset.UtcNow.AddMinutes(2));
            var status = Assert.Single(scheduler.Status);
            Assert.Equal("completed", status.State);
            Assert.Equal(300, status.ScannedEntries);
            Assert.NotNull(status.StartedAt);
            Assert.Equal(2, scheduler.History.Length);
            Assert.All(scheduler.History, run => Assert.Equal("completed", run.State));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CompleteSkippedReportContainsAllEntriesWhilePreviewIsLimitedToTwenty()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-skipped-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var files = Path.Combine(root, "files");
            Directory.CreateDirectory(files);
            for (var i = 0; i < 25; i++)
                File.WriteAllText(Path.Combine(files, $"invalid;{i:000}.txt"), "content");

            var output = Path.Combine(root, "catalog.txt.gz");
            var scheduler = new CatalogIndexScheduler(
                [new IndexingJob(files, output, "Skipped", [], TimeSpan.FromDays(1))]);
            await scheduler.CheckOnceAsync(DateTimeOffset.UtcNow);
            var status = Assert.Single(scheduler.Status);
            // Unsupported legacy names are reported but do not prevent publication.
            Assert.Equal("completed", status.State);
            Assert.True(File.Exists(output));
            Assert.Equal(25, status.SkippedEntries);
            Assert.Equal(20, status.SkippedDetails!.Length);

            using var stream = scheduler.OpenSkippedReport(0);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            var report = await reader.ReadToEndAsync();
            for (var i = 0; i < 25; i++)
                Assert.Contains($"invalid;{i:000}.txt", report);
            Assert.Contains("Total ignore : 25", report);
            Assert.Null(scheduler.OpenSkippedReport(1));
        }
        finally { Directory.Delete(root, true); }
    }

}
