using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public sealed class LegacyCatalogUpdaterTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".txt.gz")]
    public void ReportsAddedModifiedRemovedAndUnchangedEntries(string extension)
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-update-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, "source");
        Directory.CreateDirectory(root);
        var unchanged = Path.Combine(root, "same.txt");
        var modified = Path.Combine(root, "changed.txt");
        var removed = Path.Combine(root, "deleted.txt");
        File.WriteAllText(unchanged, "same");
        File.WriteAllText(modified, "before");
        File.WriteAllText(removed, "remove");
        var output = Path.Combine(work, "catalog" + extension);
        try
        {
            Assert.Equal(3, LegacyCatalogWriter.Build(root, output, "Archives"));
            File.WriteAllText(modified, "changed content of different size");
            File.Delete(removed);
            File.WriteAllText(Path.Combine(root, "added.txt"), "new");
            var summary = LegacyCatalogUpdater.Update(root, output);
            Assert.Equal(3, summary.Total);
            Assert.Equal(1, summary.Added);
            Assert.Equal(1, summary.Modified);
            Assert.Equal(1, summary.Removed);
            Assert.Equal(1, summary.Unchanged);
            Assert.Equal(0, summary.Skipped);
            var catalog = LegacyCatalogReader.ReadFile(output);
            Assert.Equal("Archives", catalog.Header.Name);
            Assert.Empty(catalog.Diagnostics);
            Assert.Contains(catalog.EnumerateEntries(), x => x.Name == "added.txt");
            Assert.DoesNotContain(catalog.EnumerateEntries(), x => x.Name == "deleted.txt");
        }
        finally { Directory.Delete(work, true); }
    }


    [Fact]
    public void UnchangedLargeCatalogReportsNoChangesOnRepeatedUpdate()
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-update-repeat-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, "source");
        Directory.CreateDirectory(root);
        var output = Path.Combine(work, "catalog.txt.gz");
        try
        {
            for (var i = 0; i < 120; i++)
                File.WriteAllText(Path.Combine(root, $"file-{i:D3}.txt"), $"data-{i}");
            LegacyCatalogWriter.Build(root, output);
            for (var run = 0; run < 2; run++)
            {
                var summary = LegacyCatalogUpdater.Update(root, output);
                Assert.Equal(120, summary.Total);
                Assert.Equal(120, summary.Unchanged);
                Assert.Equal(0, summary.Added);
                Assert.Equal(0, summary.Modified);
                Assert.Equal(0, summary.Removed);
            }
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void RejectsWrongRootWithoutChangingOriginalCatalog()
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-update-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(work, "one");
        var second = Path.Combine(work, "two");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var output = Path.Combine(work, "catalog.txt.gz");
        try
        {
            LegacyCatalogWriter.Build(first, output);
            var original = File.ReadAllBytes(output);
            Assert.Throws<InvalidDataException>(() => LegacyCatalogUpdater.Update(second, output));
            Assert.Equal(original, File.ReadAllBytes(output));
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void RejectsMissingCatalogInsteadOfSilentlyCreatingOne()
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            Assert.Throws<FileNotFoundException>(() =>
                LegacyCatalogUpdater.Update(work, Path.Combine(work, "missing.txt.gz")));
        }
        finally { Directory.Delete(work, true); }
    }
    [Fact]
    public void UpdateWithExclusionsRemovesOnlyExcludedSubtrees()
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-update-exclude-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, "source");
        Directory.CreateDirectory(Path.Combine(root, "Private"));
        Directory.CreateDirectory(Path.Combine(root, "Public"));
        File.WriteAllText(Path.Combine(root, "Private", "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(root, "Public", "visible.txt"), "visible");
        var output = Path.Combine(work, "catalog.txt.gz");
        try
        {
            Assert.Equal(4, LegacyCatalogWriter.Build(root, output));
            var summary = LegacyCatalogUpdater.Update(root, output, excludePaths: ["Private"]);
            Assert.Equal(2, summary.Total);
            Assert.Equal(2, summary.Removed);
            Assert.Equal(2, summary.Unchanged);
            Assert.Equal(0, summary.Added);
            Assert.Equal(0, summary.Skipped);
            var catalog = LegacyCatalogReader.ReadFile(output);
            Assert.Equal(["Public", "visible.txt"], catalog.EnumerateEntries().Select(x => x.Name));
            Assert.Empty(catalog.Diagnostics);
        }
        finally { Directory.Delete(work, true); }
    }

}
