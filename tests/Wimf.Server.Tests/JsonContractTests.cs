using System.Text.Json;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class JsonContractTests
{
    [Fact]
    public void SearchResponseUsesExpectedJavaScriptFieldNames()
    {
        var hit = new SearchHit("Synthetic", 0, 2, "Rapport.pdf", @"C:\Docs\Rapport.pdf",
            "pdf", 123, DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(
            new SearchResponse(1, [hit]), WimfJsonContext.Default.SearchResponse);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("count").GetInt32());
        var result = Assert.Single(root.GetProperty("results").EnumerateArray());
        Assert.Equal("Rapport.pdf", result.GetProperty("name").GetString());
        Assert.Equal(@"C:\Docs\Rapport.pdf", result.GetProperty("fullPath").GetString());
        Assert.Equal("Synthetic", result.GetProperty("catalog").GetString());
        Assert.Equal(0, result.GetProperty("catalogIndex").GetInt32());
        Assert.Equal(123UL, result.GetProperty("size").GetUInt64());
    }

    [Fact]
    public void ErrorResponseUsesExpectedJavaScriptFieldName()
    {
        var json = JsonSerializer.Serialize(
            new ErrorResponse("Test error"), WimfJsonContext.Default.ErrorResponse);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Test error", document.RootElement.GetProperty("error").GetString());
    }
    [Fact]
    public void CatalogSummaryAndSearchSelectionUseCamelCaseIndices()
    {
        var summary = new CatalogSummary(2, "Archives", "/archives", 12, 0, DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(new[] { summary }, WimfJsonContext.Default.CatalogSummaryArray);
        using var catalogsDocument = JsonDocument.Parse(json);
        Assert.Equal(2, catalogsDocument.RootElement[0].GetProperty("index").GetInt32());

        var request = new SearchRequest { CatalogIndices = [2, 0], Text = "report" };
        var requestJson = JsonSerializer.Serialize(request, WimfJsonContext.Default.SearchRequest);
        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal([2, 0], requestDocument.RootElement.GetProperty("catalogIndices")
            .EnumerateArray().Select(element => element.GetInt32()).ToArray());
    }

    [Fact]
    public void RefreshStatusSerializesLocalAndSourceFailures()
    {
        var payload = new RefreshStatus(3, 1, DateTimeOffset.UnixEpoch, 1, 2);
        var json = JsonSerializer.Serialize(payload, WimfJsonContext.Default.RefreshStatus);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("failedSources").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("failedLocalCatalogs").GetInt32());
    }

}
