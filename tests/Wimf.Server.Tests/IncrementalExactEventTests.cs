using System.Text;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalExactEventTests
{
    [Fact]
    public void ExactFileEventDoesNotRecurseIntoParentDirectory()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "wimf-exact-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(basePath, "share");
        var journal = Path.Combine(basePath, "changes.events");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "other"));
            var changed = Path.Combine(root, "new.txt");
            File.WriteAllText(changed, "modified");
            File.WriteAllText(Path.Combine(root, "other", "not-targeted.txt"), "untouched");
            static string Enc(string path) => Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
            File.WriteAllLines(journal, [
                "D\t" + Enc(root),
                "E\tchanged\t" + Enc(changed)
            ]);
            var targets = new List<string>();
            var result = IncrementalScanPreview.Analyze(root, journal,
                onTarget: targets.Add);
            Assert.Equal(0, result.Directories);
            Assert.Equal(1, result.Files);
            Assert.Equal(0, result.Folders);
            Assert.Equal(0, result.InvalidRecords);
            Assert.Contains("[FILE] " + changed, targets);
            Assert.True(result.FullScanRequired);
        }
        finally { if (Directory.Exists(basePath)) Directory.Delete(basePath, true); }
    }
}
