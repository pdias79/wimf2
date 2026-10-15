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
    [Fact]
    public void ExposesSizeAndUtcDateFiltersWithValidation()
    {
        foreach (var id in new[] { "min-size", "max-size", "modified-from", "modified-to" })
            Assert.Contains("id=" + (char)34 + id + (char)34, WebUi.Html);
        Assert.Contains("payload.minSize = parseSize", WebUi.Html);
        Assert.Contains("payload.maxSize = parseSize", WebUi.Html);
        Assert.Contains("Number.isSafeInteger(size)", WebUi.Html);
        Assert.Contains("payload.minSize > payload.maxSize", WebUi.Html);
        Assert.Contains("payload.modifiedFrom = modifiedFromText", WebUi.Html);
        Assert.Contains("T00:00:00.000Z", WebUi.Html);
        Assert.Contains("T23:59:59.999Z", WebUi.Html);
        Assert.Contains("modifiedFromText > modifiedToText", WebUi.Html);
    }

}
