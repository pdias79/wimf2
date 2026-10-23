using System.Diagnostics;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Non-invasive process memory sample. Does not force GC or reload catalogs.</summary>
public static class ServerMemoryDiagnostics
{
    public sealed record Sample(DateTimeOffset CapturedAt, int CatalogCount, long EntryCount,
        long ManagedHeapBytes, long GcHeapSizeBytes, long GcCommittedBytes,
        long WorkingSetBytes, long PrivateBytes, int Gen0Collections,
        int Gen1Collections, int Gen2Collections,
        long GcFragmentedBytes = 0, long GcTotalAvailableBytes = 0,
        long VirtualMemoryBytes = 0, long PagedMemoryBytes = 0,
        long NonpagedSystemMemoryBytes = 0, long PagedSystemMemoryBytes = 0,
        long? WindowsPrivateWorkingSetBytes = null);

    public sealed record Comparison(Sample Before, Sample After,
        long ManagedHeapReclaimedBytes, long WorkingSetChangeBytes,
        long GcCommittedChangeBytes);

    private static readonly object DiagnosticGate = new();

    /// <summary>Explicit diagnostic only. This pauses managed threads for a full blocking GC.</summary>
    public static Comparison CaptureAfterFullCollection(IReadOnlyCollection<Wimf.Catalog.Catalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        lock (DiagnosticGate)
        {
            var before = Capture(catalogs);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            var after = Capture(catalogs);
            return new Comparison(before, after,
                before.ManagedHeapBytes - after.ManagedHeapBytes,
                after.WorkingSetBytes - before.WorkingSetBytes,
                after.GcCommittedBytes - before.GcCommittedBytes);
        }
    }

    public static Sample Capture(IReadOnlyCollection<Wimf.Catalog.Catalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var memory = GC.GetGCMemoryInfo();
        return new Sample(DateTimeOffset.UtcNow, catalogs.Count,
            catalogs.Sum(c => (long)c.RecordCount),
            GC.GetTotalMemory(forceFullCollection: false),
            memory.HeapSizeBytes, memory.TotalCommittedBytes,
            process.WorkingSet64, process.PrivateMemorySize64,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            memory.FragmentedBytes, memory.TotalAvailableMemoryBytes,
            process.VirtualMemorySize64, process.PagedMemorySize64,
            process.NonpagedSystemMemorySize64, process.PagedSystemMemorySize64,
            WindowsProcessMemory.TryPrivateWorkingSetBytes());
    }
}
