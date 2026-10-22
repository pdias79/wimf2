using System.Diagnostics;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Single-catalog, isolated and repeatable measurement; never publishes or indexes.</summary>
public static class CatalogMemoryBenchmark
{
    public sealed record Snapshot(
        int Entries, long CatalogFileBytes, long ManagedBaselineBytes,
        long ManagedLoadedBytes, long ManagedDeltaBytes,
        long WorkingSetBaselineBytes, long WorkingSetLoadedBytes,
        long LoadMilliseconds);

    public static Snapshot Measure(string catalogPath)
    {
        var path = Path.GetFullPath(catalogPath);
        var fileBytes = new FileInfo(path).Length;
        ForceCollection();
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        var timer = Stopwatch.StartNew();
        var catalog = LegacyCatalogReader.ReadFile(path);
        timer.Stop();
        ForceCollection();
        var loaded = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetAfter = Process.GetCurrentProcess().WorkingSet64;
        var result = new Snapshot(catalog.RecordCount, fileBytes,
            baseline, loaded, loaded - baseline, workingSetBefore, workingSetAfter,
            timer.ElapsedMilliseconds);
        GC.KeepAlive(catalog);
        return result;
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
