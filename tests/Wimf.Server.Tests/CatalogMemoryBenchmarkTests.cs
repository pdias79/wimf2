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
            Assert.True(snapshot.ManagedBaselineBytes >= 0);
            Assert.True(snapshot.ManagedLoadedBytes >= 0);
            Assert.True(snapshot.LoadMilliseconds >= 0);
            Assert.Equal(before, File.ReadAllBytes(catalog));
        }
        finally { Directory.Delete(directory, true); }
    }
}
