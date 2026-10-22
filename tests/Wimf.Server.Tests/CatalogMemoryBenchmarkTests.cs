using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class CatalogMemoryBenchmarkTests
{
    [Fact]
    public void MeasuresExistingCatalogWithoutModifyingIt()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "wimf-memory-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "source");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "test.pdf"), "test");
            var catalog = Path.Combine(directory, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalog);
            var before = File.ReadAllBytes(catalog);

            var snapshot = CatalogMemoryBenchmark.Measure(catalog);

            Assert.Equal(1, snapshot.Entries);
            Assert.Equal(before.LongLength, snapshot.CatalogFileBytes);
            // GC heap measurements can fluctuate under parallel test execution;
            // do not require them to be positive or monotonically increasing.
            Assert.Equal(snapshot.ManagedLoadedBytes - snapshot.ManagedBaselineBytes,
                snapshot.ManagedDeltaBytes);
            Assert.True(snapshot.LoadMilliseconds >= 0);
            Assert.Equal(before, File.ReadAllBytes(catalog));
        }
        finally { Directory.Delete(directory, true); }
    }
}
