using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalSafeRefreshTests
{
    [Fact]
    public void AutomaticallyFallsBackOnMissingJournalChangesWithoutReplacingSource()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-safe-refresh-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var original = Path.Combine(temp, "original.txt.gz");
        var output = Path.Combine(temp, "safe.txt.gz");
        var journal = Path.Combine(temp, "events");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "old.txt"), "old");
            LegacyCatalogWriter.Build(root, original);
            var bytes = File.ReadAllBytes(original);
            File.WriteAllText(Path.Combine(root, "new.txt"), "new");
            File.WriteAllText(journal, "");
            var result = IncrementalSafeRefresh.Run(root, original, journal, output);
            Assert.Equal("full-fallback", result.Mode);
            Assert.Equal(2, result.Entries);
            Assert.Equal(bytes, File.ReadAllBytes(original));
            Assert.Empty(LegacyCatalogReader.ReadFile(output).Diagnostics);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
