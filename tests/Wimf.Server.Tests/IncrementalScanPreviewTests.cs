using System.Text;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class IncrementalScanPreviewTests
{
    [Fact]
    public void CountsOnlyDirtySubtreeAndRejectsOutsideJournalRecords()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-preview-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var dirty = Path.Combine(root, "dirty");
        var clean = Path.Combine(root, "clean");
        var journal = Path.Combine(temp, "journal.events");
        try
        {
            Directory.CreateDirectory(dirty);
            Directory.CreateDirectory(clean);
            File.WriteAllText(Path.Combine(dirty, "a.txt"), "a");
            File.WriteAllText(Path.Combine(clean, "b.txt"), "b");
            static string Event(string path) => "D\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(path));
            File.WriteAllLines(journal, [Event(dirty), Event(dirty), Event(temp), "broken"]);
            var result = IncrementalScanPreview.Analyze(root, journal);
            Assert.Equal(1, result.Directories);
            Assert.Equal(1, result.Files);
            Assert.Equal(1, result.Folders);
            Assert.Equal(2, result.InvalidRecords);
            Assert.True(result.FullScanRequired);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public void MissingDirtyDirectoryNeedsReconciliation()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-preview-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var journal = Path.Combine(temp, "journal.events");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(journal, "D\t" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.Combine(root, "removed"))));
            var result = IncrementalScanPreview.Analyze(root, journal);
            Assert.Equal(1, result.Skipped);
            Assert.True(result.FullScanRequired);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
