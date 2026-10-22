using System.Text;
using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public sealed class DirectCompactReaderTests
{
    [Fact]
    public void ReadsSplitSuffixesAndPreservesMalformedLegacyIds()
    {
        var raw = "[WIMF Catalog]\nSample\n/root\n0\npdf\n" +
            "dir;folder;0;0;1\n" +
            "a.pdf;pdf;12;1;2\n" +
            "bad-entry\n" +
            "b.PDF;PDF;8;1;3\n" +
            "noextension;;7;1;4\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var catalog = LegacyCatalogReader.Read(stream, false);
        Assert.Equal(5, catalog.RecordCount);
        Assert.Equal("a.pdf", catalog.GetEntry(2)!.Name);
        Assert.Null(catalog.GetEntry(3));
        Assert.Equal("b.PDF", catalog.GetEntry(4)!.Name);
        Assert.Equal("noextension", catalog.GetEntry(5)!.Name);
        Assert.Equal("", catalog.GetEntry(5)!.Extension);
        Assert.Single(catalog.Diagnostics);
        Assert.Equal(8, catalog.Diagnostics[0].LineNumber);
        Assert.Equal("/root/dir/a.pdf", catalog.GetFullPath(2));
    }

    [Fact]
    public void InvalidParentIsReportedWithoutDiscardingEntry()
    {
        var raw = "[WIMF Catalog]\nSample\n/root\n0\npdf\n" +
            "a.pdf;pdf;12;99;2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var catalog = LegacyCatalogReader.Read(stream, false);
        Assert.Equal("a.pdf", catalog.GetEntry(1)!.Name);
        Assert.Contains(catalog.Diagnostics, d => d.Message.Contains("Invalid parent reference 99"));
    }
}
