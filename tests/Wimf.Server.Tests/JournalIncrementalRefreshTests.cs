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
}
