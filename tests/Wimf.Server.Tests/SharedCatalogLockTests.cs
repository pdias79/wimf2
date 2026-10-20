using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class SharedCatalogLockTests
{
    [Fact]
    public void OnlyOneHolderMayAcquireLockAndReleaseAllowsAnother()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = Path.Combine(directory, "database.txt.gz");
            using (var first = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)))
            {
                Assert.NotNull(first);
                Assert.Null(SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)));
            }
            using (var second = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)))
            {
                Assert.NotNull(second);
                Assert.True(File.Exists(catalog + ".wimf.lock"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void OldMarkerDoesNotOverrideAnActiveOperatingSystemLock()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = Path.Combine(directory, "database.txt.gz");
            using (var first = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)))
            {
                Assert.NotNull(first);
                File.SetLastWriteTimeUtc(catalog + ".wimf.lock", DateTime.UtcNow.AddHours(-2));
                Assert.Null(SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void InvalidExpiryIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SharedCatalogLock.TryAcquire(path, TimeSpan.Zero));
    }
}
