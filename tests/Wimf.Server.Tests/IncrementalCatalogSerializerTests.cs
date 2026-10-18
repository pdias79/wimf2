using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalCatalogSerializerTests
{
    [Fact]
    public void WritesReadableCatalogWithDirectoryHierarchyWithoutOverwriting()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-incremental-writer-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var output = Path.Combine(temp, "candidate.txt.gz");
        try
        {
            Directory.CreateDirectory(root);
            var folder = Path.Combine(root, "folder");
            var file = Path.Combine(folder, "test.txt");
            var entries = new Dictionary<string, IncrementalCatalogSerializer.Entry>
            {
                [folder] = new("folder", 0, 21000),
                [file] = new("txt", 123, 21000)
            };
            Assert.Equal(2, IncrementalCatalogSerializer.Write(root, "Test", entries, output));
            var parsed = LegacyCatalogReader.ReadFile(output);
            Assert.Empty(parsed.Diagnostics);
            Assert.Equal(2, parsed.EnumerateEntries().Count());
            Assert.Contains(parsed.EnumerateEntries(), e => parsed.GetFullPath(e.Id) == file);
            Assert.Throws<IOException>(() =>
                IncrementalCatalogSerializer.Write(root, "Test", entries, output));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public void RejectsOrphansAndOutputInsideIndexedRoot()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-incremental-writer-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        try
        {
            Directory.CreateDirectory(root);
            var orphan = new Dictionary<string, IncrementalCatalogSerializer.Entry>
            {
                [Path.Combine(root, "missing", "file.txt")] = new("txt", 12, 21000)
            };
            Assert.Throws<InvalidDataException>(() =>
                IncrementalCatalogSerializer.Write(root, "Test", orphan, Path.Combine(temp, "candidate.txt")));
            Assert.Throws<InvalidDataException>(() =>
                IncrementalCatalogSerializer.Write(root, "Test", orphan, Path.Combine(root, "candidate.txt")));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
