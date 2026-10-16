using System.Text.Json;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class RefreshStatusUiTests
{
    [Fact]
    public void JsonStatusExposesLocalAndNetworkFailures()
    {
        var status = new RefreshStatus(3, 1, DateTimeOffset.UnixEpoch, 1, 2);
        var json = JsonSerializer.Serialize(status, WimfJsonContext.Default.RefreshStatus);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("failedSources").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("failedLocalCatalogs").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("catalogCount").GetInt32());
    }

    [Fact]
    public void InterfaceShowsRefreshFailuresAndPollsForAutomaticUpdates()
    {
        Assert.Contains("id=\"refresh-report\"", WebUi.Html);
        Assert.Contains("showRefreshReport(data.failedLocalCatalogs, data.failedSources)", WebUi.Html);
        Assert.Contains("showRefreshReport(state.failedLocalCatalogs, state.failedSources)", WebUi.Html);
        Assert.Contains("state.lastRefresh === observedRefresh", WebUi.Html);
        Assert.Contains("setInterval(checkRefreshStatus, 15000)", WebUi.Html);
        Assert.Contains("renderCatalogs(await catalogsResponse.json())", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }
}
