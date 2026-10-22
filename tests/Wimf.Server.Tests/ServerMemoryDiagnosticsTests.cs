using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class ServerMemoryDiagnosticsTests
{
    [Fact]
    public void CapturesNonInvasiveSnapshotOfEmptyCatalogSet()
    {
        var initialCollections = GC.CollectionCount(2);
        var sample = ServerMemoryDiagnostics.Capture(Array.Empty<Wimf.Catalog.Catalog>());
        Assert.Equal(0, sample.CatalogCount);
        Assert.Equal(0, sample.EntryCount);
        Assert.True(sample.WorkingSetBytes > 0);
        Assert.True(sample.PrivateBytes > 0);
        Assert.True(sample.ManagedHeapBytes >= 0);
        Assert.Equal(initialCollections, GC.CollectionCount(2));
    }

    [Fact]
    public void RejectsNullCatalogCollection() =>
        Assert.Throws<ArgumentNullException>(() => ServerMemoryDiagnostics.Capture(null!));
}
