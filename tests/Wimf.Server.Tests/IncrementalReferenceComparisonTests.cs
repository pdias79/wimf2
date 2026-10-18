using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalReferenceComparisonTests
{
    [Fact]
    public void ComparesAddedAndRemovedFilesWithoutChangingSourceCatalog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-ref-test-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var catalog = Path.Combine(temp, "previous.txt.gz");
        try
        {
            Directory.CreateDirectory(root);
            var deleted = Path.Combine(root, "deleted.txt");
            File.WriteAllText(deleted, "old");
            LegacyCatalogWriter.Build(root, catalog);
            var before = File.ReadAllBytes(catalog);
            File.Delete(deleted);
            File.WriteAllText(Path.Combine(root, "added.txt"), "new");
            var comparison = IncrementalReferenceComparison.Compare(root, catalog);
            Assert.Equal(1, comparison.Previous);
            Assert.Equal(1, comparison.Reference);
            Assert.Equal(1, comparison.Added);
            Assert.Equal(1, comparison.Removed);
            Assert.Equal(0, comparison.Modified);
            Assert.True(comparison.FullScanRequired);
            Assert.Equal(before, File.ReadAllBytes(catalog));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
    }
}
