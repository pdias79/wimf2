using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Runs independent disk indexing jobs on their configured cadence.</summary>
public sealed class CatalogIndexScheduler
{
    private readonly IndexingJob[] _jobs;
    private readonly DateTimeOffset[] _retryNotBefore;
    private readonly Func<IndexingJob, CancellationToken, Task> _execute;

    public CatalogIndexScheduler(IEnumerable<IndexingJob> jobs,
        Func<IndexingJob, CancellationToken, Task>? execute = null)
    {
        _jobs = jobs.ToArray();
        _retryNotBefore = new DateTimeOffset[_jobs.Length];
        _execute = execute ?? ((job, token) => Task.Run(() =>
        {
            if (File.Exists(job.Output))
            {
                var summary = LegacyCatalogUpdater.Update(job.Root, job.Output, job.Name,
                    cancellationToken: token, onSkipped: Console.Error.WriteLine, excludePaths: job.Exclude);
                Console.WriteLine($"Indexed '{job.Root}': {summary.Total} entries, " +
                    $"{summary.Added} added, {summary.Modified} changed, {summary.Removed} removed, " +
                    $"{summary.Skipped} skipped.");
            }
            else
            {
                var count = LegacyCatalogWriter.Build(job.Root, job.Output, job.Name,
                    cancellationToken: token, onSkipped: Console.Error.WriteLine, excludePaths: job.Exclude);
                Console.WriteLine($"Indexed '{job.Root}': {count} entries.");
            }
        }, token));
    }

    public static bool IsDue(IndexingJob job, DateTimeOffset indexedAt, DateTimeOffset now) =>
        now >= indexedAt.Add(job.Interval);

    /// <summary>Runs each due job at most once per check, with a one-hour retry delay on failure.</summary>
    public async Task CheckOnceAsync(DateTimeOffset now, CancellationToken token = default)
    {
        for (var i = 0; i < _jobs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var job = _jobs[i];
            if (now < _retryNotBefore[i]) continue;
            try
            {
                var indexedAt = File.Exists(job.Output)
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(job.Output), TimeSpan.Zero)
                    : DateTimeOffset.MinValue;
                if (!IsDue(job, indexedAt, now)) continue;
                Console.WriteLine($"Scheduled indexing started: '{job.Root}' -> '{job.Output}'.");
                await _execute(job, token);
                _retryNotBefore[i] = DateTimeOffset.MinValue;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       InvalidDataException or ArgumentException or OverflowException or
                                       System.Security.SecurityException)
            {
                _retryNotBefore[i] = DateTimeOffset.UtcNow.AddHours(1);
                Console.Error.WriteLine($"Scheduled indexing failed for '{job.Root}': {ex.Message}");
            }
        }
    }

    public async Task RunAsync(CancellationToken token)
    {
        await CheckOnceAsync(DateTimeOffset.UtcNow, token);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(token))
            await CheckOnceAsync(DateTimeOffset.UtcNow, token);
    }
}
