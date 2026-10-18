using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalCandidateComparisonTests
{
    [Fact]
    public void AppliesFileCreationAndDeletionWithoutModifyingInputCatalog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-candidate-test-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var catalog = Path.Combine(temp, "baseline.txt.gz");
        var journal = Path.Combine(temp, "events");
        try
        {
            Directory.CreateDirectory(root);
            var old = Path.Combine(root, "old.txt");
            var added = Path.Combine(root, "new.txt");
            File.WriteAllText(old, "old");
            LegacyCatalogWriter.Build(root, catalog);
            var previous = File.ReadAllBytes(catalog);
            File.Delete(old);
            File.WriteAllText(added, "new");
            static string Event(string action, string path) => "E\t" + action + "\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
            File.WriteAllLines(journal, [Event("deleted", old), Event("created", added)]);
            var result = IncrementalCandidateComparison.Compare(root, catalog, journal);
            Assert.True(result.Equal);
            Assert.Equal(0, result.Missing);
            Assert.Equal(0, result.Extra);
            Assert.Equal(0, result.Different);
            Assert.Equal(previous, File.ReadAllBytes(catalog));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void MissingEventLeavesDetectableMismatch()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-candidate-test-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var catalog = Path.Combine(temp, "baseline.txt.gz");
        var journal = Path.Combine(temp, "events");
        try
        {
            Directory.CreateDirectory(root);
            LegacyCatalogWriter.Build(root, catalog);
            File.WriteAllText(Path.Combine(root, "missed.txt"), "new");
            File.WriteAllText(journal, "");
            var result = IncrementalCandidateComparison.Compare(root, catalog, journal);
            Assert.False(result.Equal);
            Assert.Equal(1, result.Missing);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
    }
}
