using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class FilenameSuffixProfileTests
{
    [Fact]
    public void MeasuresOnlyExactReconstructibleSuffixes()
    {
        const string input = """
            [WIMF Catalog]
            Test
            /root
            0
            pdf txt
            directory;folder;0;0;1
            report.pdf;pdf;1;1;1
            scan.PDF;PDF;1;1;1
            wrong.pdf;txt;1;1;1
            noextension;;1;1;1
            .pdf;pdf;1;1;1
            notes.txt;txt;1;1;1
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var catalog = LegacyCatalogReader.Read(stream, false);
        var result = FilenameSuffixProfile.Analyze([catalog]);
        Assert.Equal(7, result.Entries);
        Assert.Equal(3, result.SuffixMatches);
        Assert.Equal(2L * (4 + 4 + 4), result.PotentialUtf16Bytes);
        Assert.Equal(2, result.Catalogs[0].NonMatchingExtensions);
        Assert.Equal(6, result.Catalogs[0].FileEntries);
    }

    [Fact]
    public void RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => FilenameSuffixProfile.Analyze(null!));
}
