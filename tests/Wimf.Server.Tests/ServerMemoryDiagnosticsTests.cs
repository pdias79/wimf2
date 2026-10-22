using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class ServerMemoryDiagnosticsTests
{
    [Fact]
    public void CapturesNonInvasiveSnapshotOfEmptyCatalogSet()
    {
        var sample = ServerMemoryDiagnostics.Capture(Array.Empty<Wimf.Catalog.Catalog>());
        Assert.Equal(0, sample.CatalogCount);
        Assert.Equal(0, sample.EntryCount);
        Assert.True(sample.WorkingSetBytes > 0);
        Assert.True(sample.PrivateBytes > 0);
        // GC.GetTotalMemory(false) is a best-effort sample and may transiently be negative.
        // Other tests and the runtime may collect concurrently; counters are observational.
        Assert.True(sample.Gen2Collections >= 0);
        Assert.True(sample.Gen1Collections >= 0);
        Assert.True(sample.Gen0Collections >= 0);
    }

    [Fact]
    public void ExplicitCollectionReturnsComparableSnapshots()
    {
        var result = ServerMemoryDiagnostics.CaptureAfterFullCollection(
            Array.Empty<Wimf.Catalog.Catalog>());
        Assert.Equal(0, result.Before.EntryCount);
        Assert.Equal(0, result.After.EntryCount);
        Assert.True(result.After.Gen2Collections >= result.Before.Gen2Collections + 1);
        Assert.Equal(result.Before.ManagedHeapBytes - result.After.ManagedHeapBytes,
            result.ManagedHeapReclaimedBytes);
        Assert.Equal(result.After.WorkingSetBytes - result.Before.WorkingSetBytes,
            result.WorkingSetChangeBytes);
    }

    [Fact]
    public void ExplicitCollectionRejectsNull() =>
        Assert.Throws<ArgumentNullException>(() =>
            ServerMemoryDiagnostics.CaptureAfterFullCollection(null!));

    [Fact]
    public void RejectsNullCatalogCollection() =>
        Assert.Throws<ArgumentNullException>(() => ServerMemoryDiagnostics.Capture(null!));
}
