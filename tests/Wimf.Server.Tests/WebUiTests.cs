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

    [Fact]
    public void ResultTableOffersLocalStableSortingAndDates()
    {
        Assert.Contains("id=" + (char)34 + "result-sort" + (char)34, WebUi.Html);
        foreach (var mode in new[]
        {
            "original", "name-asc", "name-desc", "size-asc", "size-desc",
            "date-asc", "date-desc"
        })
            Assert.Contains("value=" + (char)34 + mode + (char)34, WebUi.Html);
        Assert.Contains("Modifié le", WebUi.Html);
        Assert.Contains("new Intl.DateTimeFormat", WebUi.Html);
        Assert.Contains("timeZone: " + (char)34 + "UTC" + (char)34, WebUi.Html);
        Assert.Contains("nameOrder.compare", WebUi.Html);
        Assert.Contains("order || a.index - b.index", WebUi.Html);
        Assert.Contains("sortControl.addEventListener(" + (char)34 + "change" + (char)34, WebUi.Html);
        Assert.Contains("const hits = sortHits(currentHits)", WebUi.Html);
        Assert.Contains("activeController !== controller", WebUi.Html);
    }

    [Fact]
    public void PaginationAndCsvExportAreLocalAndSafe()
    {
        foreach (var id in new[] { "pagination", "page-prev", "page-next", "page-size", "page-status", "export-csv" })
            Assert.Contains("id=" + (char)34 + id + (char)34, WebUi.Html);
        Assert.Contains("hits.slice(start, start + pageSize)", WebUi.Html);
        Assert.Contains("currentPage = Math.min(currentPage, totalPages - 1)", WebUi.Html);
        Assert.Contains("previousButton.disabled = currentPage === 0", WebUi.Html);
        Assert.Contains("sortHits(currentHits).map(hit =>", WebUi.Html);
        Assert.Contains("function csvField(value)", WebUi.Html);
        Assert.Contains("safe.replaceAll", WebUi.Html);
        Assert.Contains("URL.createObjectURL(new Blob", WebUi.Html);
        Assert.Contains("wimf-resultats.csv", WebUi.Html);
        Assert.Contains("text/csv;charset=utf-8", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResultActionsDistinguishPathTextFromFileClipboard()
    {
        Assert.Contains("Copier le chemin", WebUi.Html);
        Assert.Contains("Copier le fichier", WebUi.Html);
        Assert.Contains("Afficher le dossier", WebUi.Html);
        Assert.Contains("Ouvrir", WebUi.Html);
        Assert.Contains("navigator.clipboard.writeText(hit.fullPath)", WebUi.Html);
        Assert.Contains("fileAction(\"copy\"", WebUi.Html);
        Assert.Contains("fileAction(\"open\"", WebUi.Html);
        Assert.Contains("fileAction(\"reveal\"", WebUi.Html);
        Assert.Contains("/api/file-action", WebUi.Html);
        Assert.Contains("catalogIndex: hit.catalogIndex", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

}
