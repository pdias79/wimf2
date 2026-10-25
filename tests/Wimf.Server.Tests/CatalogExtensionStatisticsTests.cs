using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class CatalogExtensionStatisticsTests
{
    [Fact]
    public void CountsFilesByExtensionDescendingAndExcludesDirectories()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-stats-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        try
        {
            File.WriteAllText(Path.Combine(root, "a.PDF"), "a");
            File.WriteAllText(Path.Combine(root, "b.pdf"), "b");
            File.WriteAllText(Path.Combine(root, "sub", "c.docx"), "c");
            File.WriteAllText(Path.Combine(root, "noext"), "d");
            var catalogPath = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalogPath);
            var catalog = LegacyCatalogReader.ReadFile(catalogPath);
            var stats = CatalogExtensionStatistics.Calculate(catalog);
            Assert.Equal(4, stats.FileCount);
            Assert.Equal(["pdf", "(sans extension)", "docx"],
                stats.Extensions.Select(item => item.Extension).ToArray());
            Assert.Equal(2, stats.Extensions[0].Count);
            Assert.Equal(4, stats.Extensions.Sum(item => item.Count));
            Assert.DoesNotContain(stats.Extensions, item => item.Extension == "folder");
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
    [Fact]
    public void BenchmarkReportsFileCountWithoutMutatingCatalog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-stats-bench-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.PDF"), "a");
            File.WriteAllText(Path.Combine(root, "b.pdf"), "b");
            var catalogPath = Path.Combine(temp, "catalog.txt.gz");
            LegacyCatalogWriter.Build(root, catalogPath);
            var original = File.ReadAllBytes(catalogPath);
            var catalog = LegacyCatalogReader.ReadFile(catalogPath);
            var result = CatalogExtensionStatisticsBenchmark.Measure(catalog);
            Assert.Equal(2, result.Files);
            Assert.Equal(1, result.DistinctExtensions);
            Assert.True(result.ElapsedMilliseconds >= 0);
            Assert.True(result.AllocatedBytes >= 0);
            Assert.Equal(original, File.ReadAllBytes(catalogPath));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

}
