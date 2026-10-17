using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Runs independent disk indexing jobs on their configured cadence.</summary>
public sealed record IndexingJobStatus(string Name, string Root, string Output,
    string State, DateTimeOffset? LastSuccess, string? LastError,
    DateTimeOffset? LastUpdate = null, DateTimeOffset? NextUpdate = null,
    int? FileCount = null, bool CanStart = true,
    int ScannedEntries = 0, DateTimeOffset? StartedAt = null,
    int SkippedEntries = 0, string[]? SkippedDetails = null,
    string[]? EffectiveExclusions = null, DateTimeOffset? QueuedAt = null,
    CatalogScanMetrics? ScanMetrics = null);

public sealed record IndexingRunHistoryEntry(string Name, string State, DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt, int ScannedEntries, int SkippedEntries, string? Error);

public sealed class CatalogIndexScheduler
{
    private readonly IndexingJob[] _jobs;
    private readonly DateTimeOffset[] _retryNotBefore;
    private readonly Func<IndexingJob, CancellationToken, Task> _execute;
    private readonly Func<IndexingJob, CancellationToken, Task>? _afterSuccess;
    private readonly IndexingJobStatus[] _statuses;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private readonly int[] _manualQueued;
    private readonly object _cancellationLock = new();
    private readonly CancellationTokenSource?[] _cancellations;
    private readonly object _historyLock = new();
    private readonly List<IndexingRunHistoryEntry> _history = [];

    public IndexingRunHistoryEntry[] History
    {
        get { lock (_historyLock) return _history.ToArray(); }
    }

    private void RecordRun(int i, string state, string? error = null)
    {
        var status = Volatile.Read(ref _statuses[i]);
        var finished = DateTimeOffset.UtcNow;
        lock (_historyLock)
        {
            _history.Insert(0, new IndexingRunHistoryEntry(status.Name, state,
                status.StartedAt ?? finished, finished, status.ScannedEntries,
                status.SkippedEntries, error));
            if (_history.Count > 50) _history.RemoveRange(50, _history.Count - 50);
        }
    }

    public CatalogIndexScheduler(IEnumerable<IndexingJob> jobs,
        Func<IndexingJob, CancellationToken, Task>? execute = null,
        Func<IndexingJob, CancellationToken, Task>? afterSuccess = null)
    {
        _jobs = jobs.ToArray();
        _afterSuccess = afterSuccess;
        _retryNotBefore = new DateTimeOffset[_jobs.Length];
        _manualQueued = new int[_jobs.Length];
        _cancellations = new CancellationTokenSource?[_jobs.Length];
        _statuses = _jobs.Select(job => new IndexingJobStatus(job.Name ?? job.Root,
            job.Root, job.Output, "pending", null, null,
            EffectiveExclusions: new CatalogPathExclusions(job.Root, job.Exclude).Paths.ToArray())).ToArray();
        _execute = execute ?? ((job, token) => Task.Run(() =>
        {
            var jobIndex = Array.FindIndex(_jobs, candidate => ReferenceEquals(candidate, job));
            // Stream the complete diagnostic report to disk rather than retaining it in memory.
            var reportPath = job.Output + ".skipped-report.txt";
            using var report = new StreamWriter(new FileStream(reportPath, FileMode.Create,
                FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false));
            report.WriteLine("Wimf2 - elements ignores");
            report.WriteLine("Catalogue : " + (job.Name ?? job.Root));
            report.WriteLine("Source : " + job.Root);
            report.WriteLine();
            Action<int> progress = count =>
            {
                if (jobIndex >= 0)
                    Volatile.Write(ref _statuses[jobIndex],
                        Volatile.Read(ref _statuses[jobIndex]) with { ScannedEntries = count });
            };
            Action<string> skipped = message =>
            {
                report.WriteLine(message);
                if (jobIndex >= 0)
                {
                    // Keep a bounded sample to avoid unbounded memory use on large scans.
                    var status = Volatile.Read(ref _statuses[jobIndex]);
                    var details = status.SkippedDetails ?? [];
                    Volatile.Write(ref _statuses[jobIndex], status with
                    {
                        SkippedEntries = status.SkippedEntries + 1,
                        SkippedDetails = details.Length < 20 ? [.. details, message] : details
                    });
                }
            };
            // Opt-in experiment: 1 (default), 2 or 4 concurrent directory enumerations.
            var prefetch = int.TryParse(Environment.GetEnvironmentVariable("WIMF_INDEX_PREFETCH"),
                out var requested) && requested is 2 or 4 ? requested : 1;
            var bench = System.Diagnostics.Stopwatch.StartNew();
            CatalogScanMetrics? scanResult = null;
            Action<CatalogScanMetrics> metrics = m =>
            {
                scanResult = m;
                if (jobIndex >= 0)
                    Volatile.Write(ref _statuses[jobIndex], Volatile.Read(ref _statuses[jobIndex]) with { ScanMetrics = m });
            };
            if (File.Exists(job.Output))
            {
                var summary = LegacyCatalogUpdater.Update(job.Root, job.Output, job.Name,
                    cancellationToken: token, onSkipped: skipped,
                    excludePaths: job.Exclude, onProgress: progress, onMetrics: metrics, directoryPrefetch: prefetch);

            }
            else
            {
                var count = LegacyCatalogWriter.Build(job.Root, job.Output, job.Name,
                    cancellationToken: token, onSkipped: skipped,
                    excludePaths: job.Exclude, onProgress: progress,
                    onMetrics: metrics, directoryPrefetch: prefetch);

            }
            bench.Stop();
            if (scanResult is not null)
                Console.WriteLine($"[BENCH] {job.Root} | prefetch={prefetch} | total={bench.ElapsedMilliseconds} ms | " +
                    $"dossiers={scanResult.DirectoryCount} | enumeration={scanResult.DirectoryEnumerationMilliseconds} ms | " +
                    $"tri={scanResult.DirectorySortMilliseconds} ms | metadonnees={scanResult.MetadataMilliseconds} ms | " +
                    $"autres/ecriture={(scanResult.OutputMilliseconds?.ToString() ?? "n/a")} ms | ignores=" +
                    (jobIndex < 0 ? 0 : Volatile.Read(ref _statuses[jobIndex]).SkippedEntries));
            report.WriteLine();
            report.WriteLine("Total ignore : " + (jobIndex < 0 ? 0 : Volatile.Read(ref _statuses[jobIndex]).SkippedEntries));
        }, token));
    }

    public Stream? OpenSkippedReport(int index)
    {
        if (index < 0 || index >= _jobs.Length) return null;
        var status = Volatile.Read(ref _statuses[index]);
        if (status.State != "completed" || status.SkippedEntries == 0) return null;
        var path = _jobs[index].Output + ".skipped-report.txt";
        try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public IndexingJobStatus[] Status
    {
        get
        {
            return _jobs.Select((job, i) =>
            {
                var last = File.Exists(job.Output)
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(job.Output), TimeSpan.Zero)
                    : (DateTimeOffset?)null;
                var status = Volatile.Read(ref _statuses[i]);
                var scheduled = last?.Add(job.Interval) ?? DateTimeOffset.UtcNow;
                var next = scheduled > _retryNotBefore[i] ? scheduled : _retryNotBefore[i];
                return status with
                {
                    LastUpdate = last,
                    NextUpdate = next,
                    CanStart = Volatile.Read(ref _manualQueued[i]) == 0 && status.State != "running"
                };
            }).ToArray();
        }
    }

    /// <summary>Cancel a waiting manual request or ask a running scan to stop.</summary>
    public bool TryCancel(int index)
    {
        if (index < 0 || index >= _jobs.Length) return false;
        lock (_cancellationLock)
        {
            var status = Volatile.Read(ref _statuses[index]);
            var source = _cancellations[index];
            if (source is null || status.State is not ("queued" or "running")) return false;
            source.Cancel();
            if (status.State == "queued")
                Volatile.Write(ref _statuses[index], status with
                { State = "cancelled", QueuedAt = null, LastError = null });
            return true;
        }
    }

    /// <summary>Queue manual indexing without blocking an HTTP request on a long scan.</summary>
    public bool TryQueueManual(int index, CancellationToken shutdown)
    {
        if (index < 0 || index >= _jobs.Length) return false;
        if (Volatile.Read(ref _statuses[index]).State == "running" ||
            Interlocked.CompareExchange(ref _manualQueued[index], 1, 0) != 0)
            return false;
        CancellationTokenSource source;
        lock (_cancellationLock)
        {
            source = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            _cancellations[index] = source;
            Volatile.Write(ref _statuses[index], _statuses[index] with
            { State = "queued", LastError = null, QueuedAt = DateTimeOffset.UtcNow });
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await _executionLock.WaitAsync(source.Token);
                try { source.Token.ThrowIfCancellationRequested(); await ExecuteAsync(index, DateTimeOffset.UtcNow, source.Token); }
                finally { _executionLock.Release(); }
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested) { }
            finally
            {
                lock (_cancellationLock)
                {
                    if (ReferenceEquals(_cancellations[index], source))
                        _cancellations[index] = null;
                    Volatile.Write(ref _manualQueued[index], 0);
                }
                source.Dispose();
            }
        }, CancellationToken.None);
        return true;
    }

    private async Task ExecuteAsync(int i, DateTimeOffset now, CancellationToken token)
    {
        var job = _jobs[i];
        CancellationTokenSource? scheduledSource = null;
        lock (_cancellationLock)
        {
            if (_cancellations[i] is null)
            {
                scheduledSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                _cancellations[i] = scheduledSource;
                token = scheduledSource.Token;
            }
        }
        try
        {
        // Capture this attempt even when its source is unavailable before scanning starts.
        Volatile.Write(ref _statuses[i], _statuses[i] with
        {
            StartedAt = DateTimeOffset.UtcNow, ScannedEntries = 0, SkippedEntries = 0,
            SkippedDetails = []
        });
        try
        {
            if (!Directory.Exists(job.Root))
                throw new DirectoryNotFoundException($"Indexing root unavailable: {job.Root}");

            Volatile.Write(ref _statuses[i], _statuses[i] with
            {
                State = "running", LastError = null, QueuedAt = null, ScanMetrics = null, ScannedEntries = 0, SkippedEntries = 0, SkippedDetails = [],
                StartedAt = DateTimeOffset.UtcNow
            });
            await _execute(job, token);
            Volatile.Write(ref _statuses[i], _statuses[i] with
            {
                State = "completed", LastSuccess = DateTimeOffset.UtcNow, LastError = null
            });
            RecordRun(i, "completed");

            _retryNotBefore[i] = DateTimeOffset.MinValue;
            // A catalog is fully published before refreshing the search snapshot.
            // Refresh failures must never make an otherwise successful scan "failed".
            if (_afterSuccess is not null)
            {
                try { await _afterSuccess(job, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Unable to reload indexed catalog '{job.Output}': {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Volatile.Write(ref _statuses[i], _statuses[i] with
            { State = "cancelled", QueuedAt = null });
            RecordRun(i, "cancelled");
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or ArgumentException or OverflowException or
                                   System.Security.SecurityException)
        {
            var offline = !Directory.Exists(job.Root);
            _retryNotBefore[i] = now.Add(offline ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1));
            Volatile.Write(ref _statuses[i], _statuses[i] with { State = "failed", LastError = ex.Message });
            RecordRun(i, "failed", ex.Message);
            Console.Error.WriteLine($"Indexing failed for '{job.Root}' -> '{job.Output}': " +
                $"{ex.Message} (retry after {_retryNotBefore[i]:O}).");
        }
        }
        finally
        {
            if (scheduledSource is not null)
            {
                lock (_cancellationLock)
                {
                    if (ReferenceEquals(_cancellations[i], scheduledSource))
                        _cancellations[i] = null;
                }
                scheduledSource.Dispose();
            }
        }
    }

    public static bool IsDue(IndexingJob job, DateTimeOffset indexedAt, DateTimeOffset now) =>
        now >= indexedAt.Add(job.Interval);

    /// <summary>Runs due jobs sequentially, without competing with manual indexing.</summary>
    public async Task CheckOnceAsync(DateTimeOffset now, CancellationToken token = default)
    {
        await _executionLock.WaitAsync(token);
        try
        {
            for (var i = 0; i < _jobs.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _manualQueued[i]) != 0 || now < _retryNotBefore[i]) continue;
                var job = _jobs[i];
                var indexedAt = File.Exists(job.Output)
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(job.Output), TimeSpan.Zero)
                    : DateTimeOffset.MinValue;
                if (IsDue(job, indexedAt, now))
                    await ExecuteAsync(i, now, token);
            }
        }
        finally { _executionLock.Release(); }
    }

    public async Task RunAsync(CancellationToken token)
    {
        await CheckOnceAsync(DateTimeOffset.UtcNow, token);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(token))
            await CheckOnceAsync(DateTimeOffset.UtcNow, token);
    }
}
