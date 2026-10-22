using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class ExtensionIdFeasibilityTests
{
    [Fact]
    public void CountsExactExtensionsIncludingCaseVariantsAndFolders()
    {
        const string input = "[WIMF Catalog]\nTest\n/root\n0\npdf\ndir;folder;0;0;1\na.pdf;pdf;1;1;1\nb.PDF;PDF;2;1;1\nc; ;3;1;1\n";
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var catalog = LegacyCatalogReader.Read(source, false);
        var result = ExtensionIdFeasibility.Analyze([catalog]);
        Assert.Equal(1, result.CatalogCount);
        Assert.Equal(4, result.Entries);
        Assert.Equal(4, result.MaximumDistinctExtensions);
        Assert.True(result.AllFitInByte);
        Assert.Equal(3, result.Catalogs[0].EntriesWithExtensions);
    }

    [Fact]
    public void RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => ExtensionIdFeasibility.Analyze(null!));
}
