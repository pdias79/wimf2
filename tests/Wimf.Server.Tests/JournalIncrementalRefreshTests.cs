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
            LegacyCatalogWriter.Build(root, original, exclusions: ["excluded"]);
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

}
