using System.Text;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalTypedEventTests
{
    [Fact]
    public void DeletedUnknownPathIsNotMisclassifiedAsFile()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-typed-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var journal = Path.Combine(temp, "journal.events");
        try
        {
            Directory.CreateDirectory(root);
            var missing = Path.Combine(root, "Nouveau dossier");
            var encodedRoot = Convert.ToBase64String(Encoding.UTF8.GetBytes(root));
            var encodedMissing = Convert.ToBase64String(Encoding.UTF8.GetBytes(missing));
            File.WriteAllLines(journal, [
                "D\t" + encodedRoot,
                "E\tdeleted\t" + encodedMissing,
                "K\tdeleted\tunknown\t" + encodedMissing
            ]);
            var targets = new List<string>();
            var result = IncrementalScanPreview.Analyze(root, journal, onTarget: targets.Add);
            Assert.Contains("[UNKNOWN/MISSING] " + missing, targets);
            Assert.Equal(0, result.Directories);
            Assert.Equal(0, result.Files);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(1, result.Deletes);
            Assert.Equal(1, result.Unknown);
            Assert.True(result.FullScanRequired);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
