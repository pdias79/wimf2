using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class CatalogMemoryProfileTests
{
    [Fact]
    public void MeasuresMultipleCatalogsWithoutWritingToDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                var root = Path.Combine(dir, "root" + i);
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "file" + i + ".pdf"), "data");
                var catalog = Path.Combine(dir, "catalog" + i + ".txt.gz");
                LegacyCatalogWriter.Build(root, catalog);
                paths.Add(catalog);
            }
            var originals = paths.Select(File.ReadAllBytes).ToArray();
            var result = CatalogMemoryProfile.Measure(paths);
            Assert.Equal(2, result.Catalogs);
            Assert.Equal(2, result.Entries);
            Assert.Equal(0, result.SearchMatches);
            Assert.True(result.LoadMilliseconds >= 0);
            Assert.True(result.SearchMilliseconds >= 0);
            Assert.Equal(result.FileBytes, paths.Sum(path => new FileInfo(path).Length));
            for (var i = 0; i < paths.Count; i++)
                Assert.Equal(originals[i], File.ReadAllBytes(paths[i]));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void RequiresAtLeastOneCatalog() =>
        Assert.Throws<ArgumentException>(() => CatalogMemoryProfile.Measure([]));
}
