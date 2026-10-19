namespace Wimf.Server;

public static class IndexingRunDiagnostics
{
    public sealed record Metrics(double? LatestSeconds, double? AverageSeconds,
        double SuccessRatePercent, bool UnusuallySlow);

    public static Metrics Calculate(IEnumerable<IndexingRunHistoryEntry> history)
    {
        var entries = history.ToArray();
        if (entries.Length == 0) return new(null, null, 0, false);
        static double Duration(IndexingRunHistoryEntry e) =>
            Math.Max(0, (e.FinishedAt - e.StartedAt).TotalSeconds);
        var completed = entries.Where(e => e.State == "completed").ToArray();
        var latest = entries[0];
        var previous = completed.Where(e => !ReferenceEquals(e, latest)).ToArray();
        var baseline = previous.Length == 0 ? (double?)null : previous.Average(Duration);
        var duration = Duration(latest);
        return new(duration,
            completed.Length == 0 ? null : completed.Average(Duration),
            100.0 * completed.Length / entries.Length,
            latest.State == "completed" && baseline > 0 && duration > 2 * baseline.Value);
    }
}
