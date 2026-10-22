using System.Runtime.CompilerServices;
using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public sealed class CompactCatalogEntryDataTests
{
    [Fact]
    public void InMemoryEntryUses32BytesOn64BitRuntime()
    {
        if (Environment.Is64BitProcess)
            Assert.Equal(32, Unsafe.SizeOf<CatalogEntryData>());
    }

    [Theory]
    [InlineData(-719162)]
    [InlineData(0)]
    [InlineData(17108)]
    [InlineData(2932896)]
    public void PreservesUnixDaysAndEntryData(long days)
    {
        var data = new CatalogEntryData("example.pdf", "pdf", 42, 3, days);
        Assert.Equal(days, data.ModifiedUnixDays);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(days * 86400), data.ModifiedAt);
        Assert.Equal("example.pdf", data.Name);
        Assert.Equal("pdf", data.Extension);
        Assert.Equal(42UL, data.Size);
        Assert.Equal(3, data.ParentId);
    }

    [Fact]
    public void RejectsOutOfRangePackedDays() =>
        Assert.Throws<OverflowException>(() =>
            new CatalogEntryData("bad", "txt", 0, 0, (long)int.MaxValue + 1));
}
