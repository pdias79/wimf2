using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalFallbackTests
{
    [Fact]
    public void FullFallbackBuildsSeparateReadableCatalogAndPreservesPrevious()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-fallback-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var original = Path.Combine(temp, "original.txt.gz");
        var output = Path.Combine(temp, "fallback.txt.gz");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "before.txt"), "before");
            LegacyCatalogWriter.Build(root, original);
            var originalBytes = File.ReadAllBytes(original);
            File.WriteAllText(Path.Combine(root, "after.txt"), "after");
            var result = IncrementalFallback.BuildFull(root, original, output);
            Assert.Equal("full", result.Mode);
            Assert.Equal(2, result.Entries);
            Assert.Equal(originalBytes, File.ReadAllBytes(original));
            var parsed = LegacyCatalogReader.ReadFile(output);
            Assert.Empty(parsed.Diagnostics);
            Assert.Equal(2, parsed.EnumerateEntries().Count());
            Assert.Throws<IOException>(() => IncrementalFallback.BuildFull(root, original, output));
            Assert.Throws<InvalidDataException>(() => IncrementalFallback.BuildFull(root, original, original));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
