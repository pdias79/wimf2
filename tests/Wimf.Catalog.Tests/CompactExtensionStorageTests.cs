using System.Reflection;
using System.Text;
using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public sealed class CompactExtensionStorageTests
{
    [Theory]
    [InlineData(28, "System.Byte[]")]
    [InlineData(300, "System.UInt16[]")]
    public void ChoosesCompactIdsAndPreservesExactExtensions(int extensionCount, string expectedArrayType)
    {
        var builder = new StringBuilder("[WIMF Catalog]\nExtensions\n/root\n0\npdf\n");
        builder.AppendLine("Folder;folder;0;0;1");
        for (var i = 0; i < extensionCount; i++)
            builder.AppendLine($"file{i}.ext{i};ext{i};{i + 1};1;2");
        builder.AppendLine("broken;entry");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
        var catalog = LegacyCatalogReader.Read(stream, false);
        var field = typeof(Catalog).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType.FullName == expectedArrayType);
        Assert.NotNull(field.GetValue(catalog));
        Assert.Equal(extensionCount + 2, catalog.RecordCount);
        Assert.Null(catalog.GetEntry(catalog.RecordCount));
        for (var i = 0; i < extensionCount; i++)
        {
            var entry = catalog.GetEntry(i + 2)!;
            Assert.Equal($"ext{i}", entry.Extension);
            Assert.Equal($"file{i}.ext{i}", entry.Name);
            Assert.Equal($"/root/Folder/file{i}.ext{i}", catalog.GetFullPath(i + 2));
            Assert.Equal((ulong)(i + 1), entry.Size);
        }
    }

    [Fact]
    public void KeepsCaseSensitiveAndEmptyExtensions()
    {
        const string text = "[WIMF Catalog]\nCase\n/root\n0\npdf\na.pdf;pdf;1;0;1\nb.PDF;PDF;2;0;1\nc;;3;0;1\n";
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var catalog = LegacyCatalogReader.Read(source, false);
        Assert.Equal("pdf", catalog.GetEntry(1)!.Extension);
        Assert.Equal("PDF", catalog.GetEntry(2)!.Extension);
        Assert.Equal("", catalog.GetEntry(3)!.Extension);
    }
}
