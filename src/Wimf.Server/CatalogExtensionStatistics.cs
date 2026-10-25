using System.Diagnostics;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Immutable statistics generated once per loaded catalog snapshot.</summary>
public sealed record CatalogExtensionCount(string Extension, int Count);

public sealed record CatalogExtensionStatistics(int FileCount, CatalogExtensionCount[] Extensions)
{
    public static CatalogExtensionStatistics Calculate(Wimf.Catalog.Catalog catalog)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        foreach (var entry in catalog.EnumerateEntries())
        {
            if (entry.Extension == "folder") continue;
            total++;
            // Reuse existing catalog strings while counting millions of entries.
            // The dictionary is case-insensitive; normalize only distinct keys for output.
            var extension = string.IsNullOrEmpty(entry.Extension) ? "(sans extension)" : entry.Extension;
            counts[extension] = counts.GetValueOrDefault(extension) + 1;
        }
        return new CatalogExtensionStatistics(total,
            counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new CatalogExtensionCount(pair.Key.ToLowerInvariant(), pair.Value)).ToArray());
    }
}

/// <summary>Opt-in measurement: statistics execution time and current-thread allocations.</summary>
public static class CatalogExtensionStatisticsBenchmark
{
    public sealed record Snapshot(int Files, int DistinctExtensions,
        long ElapsedMilliseconds, long AllocatedBytes);

    public static Snapshot Measure(Wimf.Catalog.Catalog catalog)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var result = CatalogExtensionStatistics.Calculate(catalog);
        timer.Stop();
        return new Snapshot(result.FileCount, result.Extensions.Length,
            timer.ElapsedMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
