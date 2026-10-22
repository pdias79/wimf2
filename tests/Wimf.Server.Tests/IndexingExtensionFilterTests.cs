using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class IndexingExtensionFilterTests
{
    [Fact]
    public void FullScanKeepsOnlySelectedFilesAndTheirAncestorDirectories()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-extensions-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        Directory.CreateDirectory(Path.Combine(root, "empty", "deep"));
        Directory.CreateDirectory(Path.Combine(root, "kept", "nested"));
        try
        {
            File.WriteAllText(Path.Combine(root, "empty", "deep", "ignored.mp4"), "no");
            File.WriteAllText(Path.Combine(root, "kept", "nested", "included.PDF"), "yes");
            File.WriteAllText(Path.Combine(root, "kept", "ignored.zip"), "no");
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            Assert.Equal(3, ScheduledCatalogPublication.Refresh(root, catalog, extensions: ["pdf"]));
            var parsed = LegacyCatalogReader.ReadFile(catalog);
            Assert.Empty(parsed.Diagnostics);
            var paths = parsed.EnumerateEntries().Select(x => parsed.GetFullPath(x.Id)).ToArray();
            Assert.Equal(3, paths.Length);
            Assert.Contains(Path.Combine(root, "kept"), paths);
            Assert.Contains(Path.Combine(root, "kept", "nested"), paths);
            Assert.Contains(Path.Combine(root, "kept", "nested", "included.PDF"), paths);
            Assert.DoesNotContain(paths, x => x.Contains("empty") || x.EndsWith(".zip"));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void IncrementalDeletionRemovesNowEmptyAncestors()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-extensions-incr-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var folder = Path.Combine(root, "folder");
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "selected.pdf");
            File.WriteAllText(file, "ok");
            var catalog = Path.Combine(temp, "catalog.txt.gz");
            ScheduledCatalogPublication.Refresh(root, catalog, extensions: ["pdf"]);
            File.Delete(file);
            File.WriteAllText(Path.Combine(folder, "excluded.zip"), "no");
            var journal = Path.Combine(temp, "events");
            File.WriteAllText(journal, "E\tdeleted\t" +
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file)) + "\n");
            var result = IncrementalJournalPublisher.Publish(root, catalog, journal, extensions: ["pdf"]);
            Assert.True(result.Changed);
            Assert.Empty(LegacyCatalogReader.ReadFile(catalog).EnumerateEntries());
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void ExplicitEmptyExtensionListProducesAnEmptyCatalog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-empty-extensions-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(dir, "source");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "file.pdf"), "hello");
            var catalog = Path.Combine(dir, "catalog.txt");
            Assert.Equal(0, ScheduledCatalogPublication.Refresh(root, catalog, extensions: []));
            Assert.Empty(LegacyCatalogReader.ReadFile(catalog).EnumerateEntries());
        }
        finally { Directory.Delete(dir, true); }
    }
}
