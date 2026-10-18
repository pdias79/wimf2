using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalCoverageAuditTests
{
    [Fact]
    public void DetectsUnreportedChangesWithoutTouchingOriginalCatalog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-coverage-test-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "share");
        var catalog = Path.Combine(temp, "original.txt.gz");
        var journal = Path.Combine(temp, "events");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "old.txt"), "x");
            LegacyCatalogWriter.Build(root, catalog);
            var before = File.ReadAllBytes(catalog);
            File.WriteAllText(Path.Combine(root, "new.txt"), "new");
            File.WriteAllText(journal, "");
            var unmatched = new List<string>();
            var report = IncrementalCoverageAudit.Analyze(root, catalog, journal, onUncovered: unmatched.Add);
            Assert.Equal(1, report.Changes);
            Assert.Equal(0, report.Covered);
            Assert.Equal(1, report.Uncovered);
            Assert.Contains(Path.Combine(root, "new.txt"), unmatched);
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.Combine(root, "new.txt")));
            File.WriteAllText(journal, "E\tcreated\t" + encoded + Environment.NewLine);
            var covered = IncrementalCoverageAudit.Analyze(root, catalog, journal);
            Assert.Equal(1, covered.Covered);
            Assert.Equal(0, covered.Uncovered);
            Assert.Equal(before, File.ReadAllBytes(catalog));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
