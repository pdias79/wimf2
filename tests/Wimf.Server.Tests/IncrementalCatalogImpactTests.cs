using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalCatalogImpactTests
{
    [Fact]
    public void DeletedDirectoryMatchesItsCatalogDescendantsWithoutChangingCatalog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-impact-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var removed = Path.Combine(root, "removed");
        var journal = Path.Combine(temp, "events.txt");
        var catalog = Path.Combine(temp, "index.txt");
        try
        {
            Directory.CreateDirectory(removed);
            File.WriteAllText(Path.Combine(removed, "child.txt"), "child");
            LegacyCatalogWriter.Build(root, catalog);
            var original = File.ReadAllBytes(catalog);
            Directory.Delete(removed, recursive: true);
            static string Enc(string path) => Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
            File.WriteAllLines(journal, [
                "E\tdeleted\t" + Enc(removed),
                "K\tdeleted\tunknown\t" + Enc(removed)
            ]);
            var result = IncrementalCatalogImpact.Analyze(root, journal, catalog);
            Assert.Equal(1, result.Paths);
            Assert.Equal(1, result.ExactMatches);
            Assert.Equal(1, result.DescendantMatches);
            Assert.Equal(2, result.CatalogEntries);
            Assert.Equal(0, result.InvalidRecords);
            Assert.Equal(original, File.ReadAllBytes(catalog));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
    }
}
