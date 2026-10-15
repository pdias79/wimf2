using Xunit;
using System.IO.Compression;
using System.Text;
using Wimf.Catalog;

namespace Wimf.Catalog.Tests;

public class LegacyCatalogReaderTests
{
    private const string Fixture = """
        [WIMF Catalog]
        Test
        C:\Data\WIMF
        1531569978
        pdf txt
        Documents;folder;0;0;17107
        report # 1.pdf;pdf;2670;1;17108
        YOUPI;ecm;0;XXXXXXXXXXXXXXXXX;0
        notes.txt;txt;123;1;17109
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsPlainAndGzipPreservingRecordIds(bool compressed)
    {
        using var stream = new MemoryStream();
        var bytes = Encoding.UTF8.GetBytes(Fixture);
        if (compressed)
        {
            using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(bytes);
        }
        else stream.Write(bytes);
        stream.Position = 0;

        var catalog = LegacyCatalogReader.Read(stream, compressed);
        Assert.Equal("Test", catalog.Header.Name);
        Assert.Equal(4, catalog.RecordCount);
        Assert.Null(catalog.GetEntry(3));
        Assert.Equal(4, catalog.GetEntry(4)!.Id);
        Assert.Equal(@"C:\Data\WIMF\Documents\notes.txt", catalog.GetFullPath(4));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(17108L * 86400),
            catalog.GetEntry(2)!.ModifiedAt);
        Assert.Single(catalog.Diagnostics);
        Assert.Equal(8, catalog.Diagnostics[0].LineNumber);
    }

    [Fact]
    public void RejectsWrongSignature()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not WIMF\n"));
        Assert.Throws<InvalidDataException>(() => LegacyCatalogReader.Read(stream, false));
    }

    [Fact]
    public void ReportsBrokenParent()
    {
        const string text = "[WIMF Catalog]\nTest\n/root\n1531569978\npdf\nfoo.pdf;pdf;1;999;17107\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var catalog = LegacyCatalogReader.Read(stream, false);
        Assert.Contains(catalog.Diagnostics, x => x.Message.Contains("Invalid parent"));
        Assert.Throws<InvalidDataException>(() => catalog.GetFullPath(1));
    }
    [Fact]
    public void CompactSlotsPreserveIdsAndMaterializedEntries()
    {
        const string fixture = "[WIMF Catalog]\\nCompact\\nC:\\Data\\n0\\npdf txt\\nFolder;folder;0;0;100\\nbad;data\\nreport.pdf;pdf;10;1;101\\nnotes.txt;txt;20;1;102\\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fixture.Replace("\\n", "\n")));
        var catalog = LegacyCatalogReader.Read(stream, false);
        Assert.Equal(4, catalog.RecordCount);
        Assert.False(catalog.TryGetEntryData(2, out _));
        Assert.True(catalog.TryGetEntryData(3, out var data));
        Assert.Equal("report.pdf", data.Name);
        Assert.Equal(1, data.ParentId);
        Assert.Equal(10UL, data.Size);
        Assert.Equal(3, catalog.GetEntry(3)!.Id);
        Assert.Equal(data.ModifiedAt, catalog.GetEntry(3)!.ModifiedAt);
        Assert.Equal(@"C:\Data\Folder\report.pdf", catalog.GetFullPath(3));
        Assert.Equal(new[] { 1, 3, 4 }, catalog.EnumerateEntries().Select(x => x.Id));
    }

    [Fact]
    public void SpanParserPreservesMalformedSlotsAndNumericParsing()
    {
        const string text = """
            [WIMF Catalog]
            Edge cases
            /data
            0
            txt
            Folder;folder;0;0;2
            valid.txt;txt;  +42 ;1; +3
            no-delimiters
            extra.txt;txt;1;1;3;unexpected
            ;txt;1;1;3
            over.txt;txt;18446744073709551616;1;3
            negative-parent.txt;txt;1;-1;3
            too-late.txt;txt;1;1;99999999999999
            last.txt;txt;1;1;3
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var catalog = LegacyCatalogReader.Read(stream, false);
        Assert.Equal(9, catalog.RecordCount);
        Assert.Equal(42UL, catalog.GetEntry(2)!.Size);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(3L * 86400), catalog.GetEntry(2)!.ModifiedAt);
        for (var id = 3; id <= 8; id++)
        {
            Assert.Null(catalog.GetEntry(id));
            Assert.False(catalog.TryGetEntryData(id, out _));
        }
        Assert.Equal(9, catalog.GetEntry(9)!.Id);
        Assert.Equal("/data/Folder/last.txt", catalog.GetFullPath(9));
        Assert.Equal(6, catalog.Diagnostics.Count);
        Assert.Equal("Entry timestamp out of range.", catalog.Diagnostics[5].Message);
    }

    [Fact]
    public void SpanParserReusesExtensionsAndKeepsTheirCase()
    {
        const string text = """
            [WIMF Catalog]
            Extensions
            /data
            0
            pdf
            one.pdf;pdf;1;0;3
            two.pdf;pdf;2;0;3
            three.PDF;PDF;3;0;3
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var catalog = LegacyCatalogReader.Read(stream, false);
        Assert.Same(catalog.GetEntry(1)!.Extension, catalog.GetEntry(2)!.Extension);
        Assert.Equal("PDF", catalog.GetEntry(3)!.Extension);
        Assert.NotSame(catalog.GetEntry(1)!.Extension, catalog.GetEntry(3)!.Extension);
    }

}
