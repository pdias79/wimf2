using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public sealed class FixedExtensionTableTests
{
    [Fact]
    public void ReservedIdsHaveStableOrderAndCapacity()
    {
        Assert.Equal(256, FixedExtensionTable.Capacity);
        Assert.Equal(new[] { "", "folder", "pdf", "doc", "docx", "txt",
            "jpg", "jpeg", "gif", "png", "ppt", "pptx", "xls", "xlsx", "pps" },
            FixedExtensionTable.Reserved);
        Assert.Equal(15, FixedExtensionTable.CreateSet().Count);
    }

    [Fact]
    public void WriterSkipsExtensionsBeyondLimitAndReportsEachSkippedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-ext-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < 243; i++)
                File.WriteAllText(Path.Combine(root, $"file{i:D3}.ex{i:D3}"), "");
            var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt.gz");
            try
            {
                var skipped = new List<string>();
                var count = LegacyCatalogWriter.Build(root, output, onSkipped: skipped.Add);
                Assert.Equal(241, count);
                Assert.Equal(2, skipped.Count);
                Assert.All(skipped, item => Assert.Contains("extension table limit (256)", item));
                Assert.Equal(241, LegacyCatalogReader.ReadFile(output).RecordCount);
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
