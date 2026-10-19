namespace Wimf.Server;

/// <summary>Pure summary from the persisted run history; no indexing side effects.</summary>
public static class IndexingPerformanceSummary
{
    public sealed record Group(string Name, string Kind, int Runs, int Failures,
        double AverageSeconds, long TotalEvents);

    public static Group[] Calculate(IEnumerable<IndexingRunHistoryEntry> entries) =>
        entries.GroupBy(e => (e.Name, e.Kind))
            .Select(group =>
            {
                var completed = group.Where(e => e.State == "completed").ToArray();
                return new Group(group.Key.Name, group.Key.Kind, group.Count(),
                    group.Count(e => e.State == "failed"),
                    completed.Length == 0 ? 0 : completed.Average(e =>
                        Math.Max(0, (e.FinishedAt - e.StartedAt).TotalSeconds)),
                    group.Sum(e => e.Kind == "incremental" ? (long)e.EventRecords : 0));
            }).OrderBy(g => g.Name, StringComparer.Ordinal)
              .ThenBy(g => g.Kind, StringComparer.Ordinal).ToArray();
}
