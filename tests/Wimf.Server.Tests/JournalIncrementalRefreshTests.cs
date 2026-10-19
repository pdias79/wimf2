using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class JournalIncrementalRefreshTests
{
    [Fact]
    public void UpdatesOnlyJournalPathsAndDoesNotReplaceOriginal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-journal-test-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(dir, "files");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "old.txt"), "a");
            var original = Path.Combine(dir, "old.txt.gz");
            LegacyCatalogWriter.Build(root, original);
            var before = File.ReadAllBytes(original);
            var journal = Path.Combine(dir, "events");
            var fresh = Path.Combine(root, "new.txt");
            File.WriteAllText(fresh, "new");
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(fresh));
            File.WriteAllText(journal, "E\tcreated\t" + encoded + "\n");
            var result = JournalIncrementalRefresh.Run(root, original, journal, Path.Combine(dir, "next.txt.gz"));
            Assert.Equal(2, result.Entries);
            Assert.Equal(1, result.Hints);
            Assert.Equal(before, File.ReadAllBytes(original));
            Assert.Equal(2, LegacyCatalogReader.ReadFile(result.Output).EnumerateEntries().Count());
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void NewFilesAndFoldersUnderExcludedDirectoryAreNotCatalogued()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-delta-exclude-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        var excluded = Path.Combine(root, "excluded");
        Directory.CreateDirectory(excluded);
        try
        {
            var original = Path.Combine(temp, "original.txt.gz");
            LegacyCatalogWriter.Build(root, original, excludePaths: ["excluded"]);
            var nested = Path.Combine(excluded, "nested");
            Directory.CreateDirectory(nested);
            var file = Path.Combine(nested, "secret.txt");
            File.WriteAllText(file, "secret");
            var journal = Path.Combine(temp, "events");
            File.WriteAllText(journal, "E\tcreated\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(excluded)) + "\n");
            var result = JournalIncrementalRefresh.Run(root, original, journal,
                Path.Combine(temp, "updated.txt.gz"), exclusions: ["excluded"]);
            Assert.Empty(LegacyCatalogReader.ReadFile(result.Output).EnumerateEntries());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void RenameDirectoryAndDeleteFileRemoveStaleCatalogPaths()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-delta-rename-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        var oldDir = Path.Combine(root, "old");
        Directory.CreateDirectory(oldDir);
        try
        {
            var oldFile = Path.Combine(oldDir, "nested.txt");
            var removed = Path.Combine(root, "removed.txt");
            File.WriteAllText(oldFile, "nested");
            File.WriteAllText(removed, "removed");
            var original = Path.Combine(temp, "original.txt.gz");
            LegacyCatalogWriter.Build(root, original);
            var newDir = Path.Combine(root, "new");
            Directory.Move(oldDir, newDir);
            File.Delete(removed);
            var journal = Path.Combine(temp, "events");
            File.WriteAllLines(journal, new[] { oldDir, newDir, removed }
                .Select(path => "E\tchanged\t" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(path))));
            var result = JournalIncrementalRefresh.Run(root, original, journal,
                Path.Combine(temp, "updated.txt.gz"));
            var catalog = LegacyCatalogReader.ReadFile(result.Output);
            var paths = catalog.EnumerateEntries().Select(e => catalog.GetFullPath(e.Id)).ToArray();
            Assert.Equal(2, paths.Length);
            Assert.Contains(newDir, paths);
            Assert.Contains(Path.Combine(newDir, "nested.txt"), paths);
            Assert.DoesNotContain(oldDir, paths);
            Assert.DoesNotContain(oldFile, paths);
            Assert.DoesNotContain(removed, paths);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

}
