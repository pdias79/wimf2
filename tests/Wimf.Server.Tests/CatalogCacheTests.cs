using System.IO.Compression;
using System.Text;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class CatalogCacheTests
{
    private static string Catalog(string name) =>
        $"[WIMF Catalog]\n{name}\nC:\\Fixture\n0\npdf\nreport.pdf;pdf;100;0;2\n";

    [Fact]
    public void CopiesAndReusesCachedCatalogWhenSourceIsOffline()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            var cacheDir = Path.Combine(root, "cache");
            File.WriteAllText(source, Catalog("First"));
            var first = CatalogCache.Load(source, cacheDir);
            Assert.False(first.UsedFallback);
            Assert.Equal("First", first.Catalog.Header.Name);
            Assert.True(File.Exists(CatalogCache.CachePath(source, cacheDir)));
            File.Delete(source);
            var fallback = CatalogCache.Load(source, cacheDir);
            Assert.True(fallback.UsedFallback);
            Assert.Equal("First", fallback.Catalog.Header.Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InvalidUpdateCannotReplaceLastValidCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            var cacheDir = Path.Combine(root, "cache");
            File.WriteAllText(source, Catalog("Valid"));
            CatalogCache.Load(source, cacheDir);
            File.WriteAllText(source, "invalid catalog");
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(2));
            var result = CatalogCache.Load(source, cacheDir);
            Assert.True(result.UsedFallback);
            Assert.Equal("Valid", result.Catalog.Header.Name);
            Assert.Equal("Valid", Wimf.Catalog.LegacyCatalogReader.ReadFile(
                CatalogCache.CachePath(source, cacheDir)).Header.Name);
            Assert.Empty(Directory.GetFiles(cacheDir, "*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void UpdatesCatalogWhenSourceChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            var cacheDir = Path.Combine(root, "cache");
            File.WriteAllText(source, Catalog("Old"));
            CatalogCache.Load(source, cacheDir);
            File.WriteAllText(source, Catalog("New version"));
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(2));
            var result = CatalogCache.Load(source, cacheDir);
            Assert.False(result.UsedFallback);
            Assert.Equal("New version", result.Catalog.Header.Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ValidatesCompressedCatalogBeforeReplacingCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.gz");
            var cacheDir = Path.Combine(root, "cache");
            using (var file = File.Create(source))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            {
                var bytes = Encoding.UTF8.GetBytes(Catalog("Compressed"));
                gzip.Write(bytes);
            }
            var result = CatalogCache.Load(source, cacheDir);
            Assert.Equal("Compressed", result.Catalog.Header.Name);
            File.WriteAllText(source, "not a gzip stream");
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(2));
            var fallback = CatalogCache.Load(source, cacheDir);
            Assert.True(fallback.UsedFallback);
            Assert.Equal("Compressed", fallback.Catalog.Header.Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
