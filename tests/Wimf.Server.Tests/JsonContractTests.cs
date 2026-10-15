using System.Text.Json;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class JsonContractTests
{
    [Fact]
    public void SearchResponseUsesExpectedJavaScriptFieldNames()
    {
        var hit = new SearchHit("Synthetic", 2, "Rapport.pdf", @"C:\Docs\Rapport.pdf",
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
}
