using System.Text.Json;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class DaemonOptionsTests
{
    [Fact]
    public void EmptyArgumentsKeepCurrentDefaults()
    {
        var options = DaemonOptions.Parse([]);
        Assert.Empty(options.Catalogs);
        Assert.Empty(options.Sources);
        Assert.Null(options.CacheDirectory);
        Assert.Equal(300, options.RefreshSeconds);
        Assert.False(options.EnableFileActions);
    }

    [Fact]
    public void ConfigResolvesRelativePathsFromItsOwnDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "wimf.json");
            File.WriteAllText(file, """
                {"catalogs":["catalogs/first.txt.gz","catalogs/second.txt.gz"],"sources":["network/share.gz"],
                 "cacheDirectory":"cache","refreshSeconds":45}
                """);
            var options = DaemonOptions.Parse(["--config", file]);
            Assert.Equal(2, options.Catalogs.Length);
            Assert.Equal(Path.Combine(directory, "catalogs", "first.txt.gz"), options.Catalogs[0]);
            Assert.Equal(Path.Combine(directory, "network", "share.gz"), options.Sources[0]);
            Assert.Equal(Path.Combine(directory, "cache"), options.CacheDirectory);
            Assert.Equal(45, options.RefreshSeconds);
            Assert.False(options.EnableFileActions);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CliOverridesListsAndSettingsButFileActionsRequireExplicitFlag()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "wimf.json");
            File.WriteAllText(file, """
                {"catalogs":["old.gz"],"sources":["old-source.gz"],"cacheDirectory":"cache","refreshSeconds":60}
                """);
            var options = DaemonOptions.Parse(["--catalog", "new.gz", "--config", file,
                "--catalog", "newer.gz", "--source", "new-source.gz",
                "--cache-dir", "other-cache", "--refresh-seconds", "0", "--enable-file-actions"]);
            Assert.Equal(["new.gz", "newer.gz"], options.Catalogs);
            Assert.Equal(["new-source.gz"], options.Sources);
            Assert.Equal("other-cache", options.CacheDirectory);
            Assert.Equal(0, options.RefreshSeconds);
            Assert.True(options.EnableFileActions);
            Assert.False(DaemonOptions.Parse(["--config", file]).EnableFileActions);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RejectsInvalidAndUnexpectedConfiguration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "wimf.json");
            File.WriteAllText(file, """{"enableFileActions":true}""");
            Assert.Throws<JsonException>(() => DaemonOptions.Parse(["--config", file]));
            File.WriteAllText(file, """{"sources":["share.gz"]}""");
            Assert.Throws<ArgumentException>(() => DaemonOptions.Parse(["--config", file]));
            File.WriteAllText(file, """{"refreshSeconds":-1}""");
            Assert.Throws<ArgumentException>(() => DaemonOptions.Parse(["--config", file]));
            File.WriteAllText(file, """{"catalogs":[""]}""");
            Assert.Throws<ArgumentException>(() => DaemonOptions.Parse(["--config", file]));
            Assert.Throws<ArgumentException>(() => DaemonOptions.Parse(["--config", file, "--config", file]));
            Assert.Throws<ArgumentException>(() => DaemonOptions.Parse(["--refresh-seconds", "-1"]));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void RelativeConfigSearchesParentDirectoriesButPrefersNearestFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-config-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "src", "Wimf.Server");
        Directory.CreateDirectory(nested);
        try
        {
            var rootConfig = Path.Combine(root, "wimf.json");
            File.WriteAllText(rootConfig, "{}");
            Assert.Equal(rootConfig, DaemonOptions.ResolveConfigurationFilePath("wimf.json", nested));
            var nestedConfig = Path.Combine(nested, "wimf.json");
            File.WriteAllText(nestedConfig, "{}");
            Assert.Equal(nestedConfig, DaemonOptions.ResolveConfigurationFilePath("wimf.json", nested));
            Assert.Equal(rootConfig, DaemonOptions.ResolveConfigurationFilePath(rootConfig, nested));
            Assert.Equal(Path.Combine(nested, "missing.json"),
                DaemonOptions.ResolveConfigurationFilePath("missing.json", nested));
        }
        finally { Directory.Delete(root, true); }
    }

}
