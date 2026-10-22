using System.Diagnostics;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Non-invasive process memory sample. Does not force GC or reload catalogs.</summary>
public static class ServerMemoryDiagnostics
{
    public sealed record Sample(DateTimeOffset CapturedAt, int CatalogCount, long EntryCount,
        long ManagedHeapBytes, long GcHeapSizeBytes, long GcCommittedBytes,
        long WorkingSetBytes, long PrivateBytes, int Gen0Collections,
        int Gen1Collections, int Gen2Collections);

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
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }
}
