using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Runs independent disk indexing jobs on their configured cadence.</summary>
public sealed record IndexingJobStatus(string Name, string Root, string Output,
    string State, DateTimeOffset? LastSuccess, string? LastError,
    DateTimeOffset? LastUpdate = null, DateTimeOffset? NextUpdate = null,
    int? FileCount = null, bool CanStart = true,
    int ScannedEntries = 0, DateTimeOffset? StartedAt = null,
    int SkippedEntries = 0, string[]? SkippedDetails = null);

public sealed class CatalogIndexScheduler
{
    private readonly IndexingJob[] _jobs;
    private readonly DateTimeOffset[] _retryNotBefore;
    private readonly Func<IndexingJob, CancellationToken, Task> _execute;
    private readonly Func<IndexingJob, CancellationToken, Task>? _afterSuccess;
    private readonly IndexingJobStatus[] _statuses;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private readonly int[] _manualQueued;

    public CatalogIndexScheduler(IEnumerable<IndexingJob> jobs,
        Func<IndexingJob, CancellationToken, Task>? execute = null,
        Func<IndexingJob, CancellationToken, Task>? afterSuccess = null)
    {
        _jobs = jobs.ToArray();
        _afterSuccess = afterSuccess;
        _retryNotBefore = new DateTimeOffset[_jobs.Length];
        _manualQueued = new int[_jobs.Length];
        _statuses = _jobs.Select(job => new IndexingJobStatus(job.Name ?? job.Root,
            job.Root, job.Output, "pending", null, null)).ToArray();
        _execute = execute ?? ((job, token) => Task.Run(() =>
        {
            var jobIndex = Array.FindIndex(_jobs, candidate => ReferenceEquals(candidate, job));
            Action<int> progress = count =>
            {
                if (jobIndex >= 0)
                    Volatile.Write(ref _statuses[jobIndex],
                        Volatile.Read(ref _statuses[jobIndex]) with { ScannedEntries = count });
            };
            Action<string> skipped = message =>
            {
                Console.Error.WriteLine(message);
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
            if (File.Exists(job.Output))
            {
                var summary = LegacyCatalogUpdater.Update(job.Root, job.Output, job.Name,
                    cancellationToken: token, onSkipped: skipped,
                    excludePaths: job.Exclude, onProgress: progress);
                Console.WriteLine($"Indexed '{job.Root}': {summary.Total} entries, " +
                    $"{summary.Added} added, {summary.Modified} changed, {summary.Removed} removed, " +
                    $"{summary.Skipped} skipped.");
            }
            else
            {
                var count = LegacyCatalogWriter.Build(job.Root, job.Output, job.Name,
                    cancellationToken: token, onSkipped: skipped,
                    excludePaths: job.Exclude, onProgress: progress);
                Console.WriteLine($"Indexed '{job.Root}': {count} entries.");
            }
        }, token));
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

    /// <summary>Queue manual indexing without blocking an HTTP request on a long scan.</summary>
    public bool TryQueueManual(int index, CancellationToken shutdown)
    {
        if (index < 0 || index >= _jobs.Length) return false;
        if (Volatile.Read(ref _statuses[index]).State == "running" ||
            Interlocked.CompareExchange(ref _manualQueued[index], 1, 0) != 0)
            return false;
        Volatile.Write(ref _statuses[index], _statuses[index] with { State = "queued", LastError = null });
        _ = Task.Run(async () =>
        {
            try
            {
                await _executionLock.WaitAsync(shutdown);
                try { await ExecuteAsync(index, DateTimeOffset.UtcNow, shutdown); }
                finally { _executionLock.Release(); }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            finally { Volatile.Write(ref _manualQueued[index], 0); }
        }, CancellationToken.None);
        return true;
    }

    private async Task ExecuteAsync(int i, DateTimeOffset now, CancellationToken token)
    {
        var job = _jobs[i];
        try
        {
            if (!Directory.Exists(job.Root))
                throw new DirectoryNotFoundException($"Indexing root unavailable: {job.Root}");
            Console.WriteLine($"Indexing started: '{job.Root}' -> '{job.Output}'.");
            Volatile.Write(ref _statuses[i], _statuses[i] with
            {
                State = "running", LastError = null, ScannedEntries = 0, SkippedEntries = 0, SkippedDetails = [],
                StartedAt = DateTimeOffset.UtcNow
            });
            await _execute(job, token);
            Volatile.Write(ref _statuses[i], _statuses[i] with
            {
                State = "completed", LastSuccess = DateTimeOffset.UtcNow, LastError = null
            });
            Console.WriteLine($"Indexing completed: '{job.Root}' -> '{job.Output}'.");
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
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or ArgumentException or OverflowException or
                                   System.Security.SecurityException)
        {
            var offline = !Directory.Exists(job.Root);
            _retryNotBefore[i] = now.Add(offline ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1));
            Volatile.Write(ref _statuses[i], _statuses[i] with { State = "failed", LastError = ex.Message });
            Console.Error.WriteLine($"Indexing failed for '{job.Root}' -> '{job.Output}': " +
                $"{ex.Message} (retry after {_retryNotBefore[i]:O}).");
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
