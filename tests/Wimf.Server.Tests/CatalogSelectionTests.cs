using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class CatalogSelectionTests
{
    [Fact]
    public void NullMeansAllCatalogsInOriginalOrder()
    {
        Assert.Equal([0, 1, 2], CatalogSelection.Resolve(3, null));
    }

    [Fact]
    public void ExplicitEmptySelectionMeansNoCatalogs()
    {
        Assert.Empty(CatalogSelection.Resolve(3, []));
    }

    [Fact]
    public void PreservesCatalogOrderRatherThanCheckboxOrder()
    {
        Assert.Equal([0, 2, 3], CatalogSelection.Resolve(4, [3, 0, 2]));
    }

    [Fact]
    public void InvalidAndDuplicateIndicesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => CatalogSelection.Resolve(2, [-1]));
        Assert.Throws<ArgumentException>(() => CatalogSelection.Resolve(2, [2]));
        Assert.Throws<ArgumentException>(() => CatalogSelection.Resolve(2, [1, 1]));
        Assert.Throws<ArgumentException>(() => CatalogSelection.Resolve(0, [0]));
        Assert.Throws<ArgumentException>(() => CatalogSelection.Resolve(1001, Enumerable.Range(0, 1001).ToArray()));
    }
}
