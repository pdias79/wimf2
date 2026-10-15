using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class WebUiTests
{
    [Fact]
    public void PageHasAccessibleSearchAndCatalogDisplay()
    {
        Assert.Contains("lang=\"fr\"", WebUi.Html);
        Assert.Contains("id=\"search-form\"", WebUi.Html);
        Assert.Contains("id=\"results\"", WebUi.Html);
        Assert.Contains("/api/search", WebUi.Html);
        Assert.Contains("/api/catalogs", WebUi.Html);
        Assert.Contains("/api/health", WebUi.Html);
    }

    [Fact]
    public void ResultsAreAddedAsTextNotHtml()
    {
        Assert.Contains("cell.textContent = String(value)", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiDoesNotRequireExternalAssets()
    {
        Assert.DoesNotContain("https://", WebUi.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script src=", WebUi.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }
}
