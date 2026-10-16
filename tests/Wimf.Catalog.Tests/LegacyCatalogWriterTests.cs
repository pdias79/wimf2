using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public sealed class LegacyCatalogWriterTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".txt.gz")]
    public void BuildsReadableCatalogWithNestedFoldersAndAccuratePaths(string extension)
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-build-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, "source");
        Directory.CreateDirectory(Path.Combine(root, "Documents", "Notes"));
        File.WriteAllText(Path.Combine(root, "Documents", "report.txt"), "Bonjour");
        File.WriteAllText(Path.Combine(root, "Documents", "Notes", "hello.md"), "Hello");
        var output = Path.Combine(work, "catalog" + extension);
        try
        {
            var count = LegacyCatalogWriter.Build(root, output, "Essai");
            Assert.Equal(4, count);
            var catalog = LegacyCatalogReader.ReadFile(output);
            Assert.Equal("Essai", catalog.Header.Name);
            Assert.Equal(root, catalog.Header.RootPath);
            Assert.Empty(catalog.Diagnostics);
            var paths = catalog.EnumerateEntries().ToDictionary(e => e.Name, e => catalog.GetFullPath(e.Id));
            Assert.Equal(Path.Combine(root, "Documents", "report.txt"), paths["report.txt"]);
            Assert.Equal(Path.Combine(root, "Documents", "Notes", "hello.md"), paths["hello.md"]);
            Assert.True(catalog.GetEntry(1)!.IsDirectory);
            Assert.Equal(7UL, catalog.EnumerateEntries().Single(e => e.Name == "report.txt").Size);
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void RejectsOutputInsideSourceAndDoesNotOverwriteIt()
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var output = Path.Combine(work, "catalog.txt");
        File.WriteAllText(output, "keep");
        try
        {
            Assert.Throws<ArgumentException>(() => LegacyCatalogWriter.Build(work, output));
            Assert.Equal("keep", File.ReadAllText(output));
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void FailsSafelyForUnsupportedSemicolonNames()
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "unsafe;file.txt"), "content");
        var output = work + ".txt.gz";
        try
        {
            Assert.Throws<InvalidDataException>(() => LegacyCatalogWriter.Build(work, output));
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(work, true);
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
