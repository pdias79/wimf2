using System.Text;
using Wimf.Catalog;
using Wimf.Search;
using Xunit;

namespace Wimf.Search.Tests;

public sealed class CompactFilenameSearchTests
{
    [Theory]
    [InlineData("report.pdf", "pdf", "report.pdf", false)]
    [InlineData("archive.tar.gz", "gz", "tar.gz", false)]
    [InlineData("scan.PDF", "pdf", "scan.PDF", false)]
    [InlineData("notes.txt", "txt", "notes.txt", true)]
    public void MatchesOriginalFullNames(string name, string extension, string query, bool regex)
    {
        var text = $"[WIMF Catalog]\nTest\n/root\n0\npdf\n{name};{extension};123;0;3\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var catalog = LegacyCatalogReader.Read(stream, false);
        Assert.Equal(name, catalog.GetEntry(1)!.Name);
        Assert.Equal("/root/" + name, catalog.GetFullPath(1));
        var engine = new SequentialSearchEngine();
        var results = engine.Search([catalog], new SearchQuery { Text = query, UseRegex = regex });
        Assert.Single(results);
        Assert.Equal(name, results[0].Entry.Name);
    }

    [Fact]
    public void KeepsMalformedSlotsAndFilesWithoutExtension()
    {
        const string text = "[WIMF Catalog]\nTest\n/root\n0\npdf\nfolder;folder;0;0;1\nreport.pdf;pdf;2;1;3\nbroken\nnoextension;;3;1;3\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var catalog = LegacyCatalogReader.Read(stream, false);
        Assert.Equal(4, catalog.RecordCount);
        Assert.Null(catalog.GetEntry(3));
        Assert.Equal("noextension", catalog.GetEntry(4)!.Name);
        Assert.Equal("/root/folder/report.pdf", catalog.GetFullPath(2));
    }
}
