using System.Reflection;
using System.Text;
using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public sealed class AdaptiveExtensionIdTests
{
    [Theory]
    [InlineData(255, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    [InlineData(300, false)]
    public void ExtensionIdsWidenWithoutLosingPreviousRecords(int count, bool expectedByte)
    {
        var input = new StringBuilder("[WIMF Catalog]\nAdaptive\n/root\n0\ntxt\n");
        for (var i = 0; i < count; i++)
            input.AppendLine($"n{i}.ext{i};ext{i};{i};0;3");
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(input.ToString()));
        var catalog = LegacyCatalogReader.Read(source, compressed: false);
        Assert.Equal(count, catalog.RecordCount);
        var field = typeof(Catalog).GetField(expectedByte ? "_byteExtensionIds" : "_shortExtensionIds",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(field.GetValue(catalog));
        var unused = typeof(Catalog).GetField(expectedByte ? "_shortExtensionIds" : "_byteExtensionIds",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Null(unused.GetValue(catalog));
        for (var i = 1; i <= count; i++)
        {
            var entry = catalog.GetEntry(i)!;
            Assert.Equal($"n{i - 1}.ext{i - 1}", entry.Name);
            Assert.Equal($"ext{i - 1}", entry.Extension);
        }
    }

    [Fact]
    public void MalformedSlotsRetainRecordIdsWhenTierWidens()
    {
        var input = new StringBuilder("[WIMF Catalog]\nAdaptive\n/root\n0\ntxt\n");
        input.AppendLine("bad");
        for (var i = 0; i < 257; i++)
            input.AppendLine($"n{i}.ext{i};ext{i};1;0;3");
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(input.ToString()));
        var catalog = LegacyCatalogReader.Read(source, false);
        Assert.Null(catalog.GetEntry(1));
        Assert.Equal("ext256", catalog.GetEntry(258)!.Extension);
    }
}
