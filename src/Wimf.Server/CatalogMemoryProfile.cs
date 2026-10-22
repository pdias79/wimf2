using System.Diagnostics;
using Wimf.Catalog;
using Wimf.Search;

namespace Wimf.Server;

/// <summary>Read-only, isolated multi-catalog measurement including a full no-match search.</summary>
public static class CatalogMemoryProfile
{
    public sealed record Report(int Catalogs, long Entries, long FileBytes,
        long ManagedBaselineBytes, long ManagedAfterLoadBytes, long ManagedAfterSearchBytes,
        long WorkingSetBeforeBytes, long WorkingSetAfterLoadBytes, long WorkingSetAfterSearchBytes,
        long LoadMilliseconds, long SearchMilliseconds, long SearchThreadAllocationsBytes,
        int SearchMatches);

    public static Report Measure(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) throw new ArgumentException("At least one catalog is required.");
        static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Collect();
        var baseline = GC.GetTotalMemory(true);
        var initialWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var catalogs = new List<Wimf.Catalog.Catalog>(paths.Count);
        var timer = Stopwatch.StartNew();
        long bytes = 0;
        foreach (var input in paths)
        {
            var path = Path.GetFullPath(input);
            bytes += new FileInfo(path).Length;
            catalogs.Add(LegacyCatalogReader.ReadFile(path));
        }
        timer.Stop();
        var loadTime = timer.ElapsedMilliseconds;
        Collect();
        var afterLoad = GC.GetTotalMemory(true);
        var loadWorkingSet = Process.GetCurrentProcess().WorkingSet64;

        // A deliberately improbable literal scans all entries while bounding retained results.
        // Search time is representative of a no-match scan, not of all user queries.
        var query = new SearchQuery { Text = "__wimf_memory_profile_no_match_9e7cda__", MaxResults = 1 };
        var engine = new SequentialSearchEngine();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        var results = engine.Search(catalogs, query);
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var searchTime = timer.ElapsedMilliseconds;
        Collect();
        var afterSearch = GC.GetTotalMemory(true);
        var searchWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var count = results.Count;
        var entries = catalogs.Sum(c => (long)c.RecordCount);
        GC.KeepAlive(catalogs);
        return new Report(catalogs.Count, entries, bytes, baseline, afterLoad, afterSearch,
            initialWorkingSet, loadWorkingSet, searchWorkingSet,
            loadTime, searchTime, allocated, count);
    }
}
