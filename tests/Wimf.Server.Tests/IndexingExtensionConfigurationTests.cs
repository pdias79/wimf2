using System.Text.Json;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class IndexingExtensionConfigurationTests
{
    [Fact]
    public void DefaultAndCustomExtensionsAreParsed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-ext-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var config = Path.Combine(dir, "wimf.json");
            File.WriteAllText(config, """{"catalogs":["cat.gz"],"indexing":[{"root":"root","output":"cat.gz","intervalDays":1}]}""");
            Assert.Equal(CatalogExtensionFilter.DefaultExtensions.OrderBy(x => x),
                Assert.Single(DaemonOptions.Parse(["--config", config]).Indexing!).Extensions!.OrderBy(x => x));
            File.WriteAllText(config, """{"catalogs":["cat.gz"],"indexing":[{"root":"root","output":"cat.gz","intervalDays":1,"extensions":[".PDF","DoCx"]}]}""");
            Assert.Equal(["docx", "pdf"], Assert.Single(DaemonOptions.Parse(["--config", config]).Indexing!).Extensions!);
            File.WriteAllText(config, """{"catalogs":["cat.gz"],"indexing":[{"root":"root","output":"cat.gz","intervalDays":1,"extensions":[]}]}""");
            Assert.Empty(Assert.Single(DaemonOptions.Parse(["--config", config]).Indexing!).Extensions!);
            File.WriteAllText(config, """{"catalogs":["cat.gz"],"indexing":[{"root":"root","output":"cat.gz","intervalDays":1,"extensions":["pdf;bad"]}]}""");
            Assert.Throws<ArgumentException>(() => DaemonOptions.Parse(["--config", config]));
        }
        finally { Directory.Delete(dir, true); }
    }
}
