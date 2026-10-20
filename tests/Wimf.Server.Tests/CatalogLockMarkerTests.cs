using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class CatalogLockMarkerTests
{
    [Fact]
    public void MissingMarkerIsReportedAsAbsent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "catalog.gz");
        Assert.False(CatalogLockMarker.Read(path).Present);
    }

    [Fact]
    public void ReadsExistingOwnerMarkerWithoutTakingOrDeletingItsLock()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var catalog = Path.Combine(folder, "catalog.gz");
            using (var held = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)))
            {
            Assert.NotNull(held);
            var result = CatalogLockMarker.Read(catalog);
            Assert.True(result.Present);
            // Windows prevents reading a byte-range locked by another handle.
            // This diagnostic must fail closed rather than interpret the marker as free.
            if (result.ReadError is null)
            {
                Assert.Contains(Environment.MachineName, result.Owner);
                Assert.NotNull(result.AcquiredUtc);
            }
            else
            {
                Assert.Null(result.Owner);
                Assert.Null(result.AcquiredUtc);
                Assert.Equal("Marqueur inaccessible", result.ReadError);
            }
            Assert.Null(SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)));
            Assert.True(File.Exists(catalog + ".wimf.lock"));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void AnOldMarkerIsStillJustAMarker()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wimf-marker-old-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var catalog = Path.Combine(folder, "catalog.gz");
            File.WriteAllText(catalog + ".wimf.lock", "owner=old-host/42\\nacquiredUtc=2026-01-01T00:00:00Z\\n");
            File.SetLastWriteTimeUtc(catalog + ".wimf.lock", DateTime.UtcNow.AddHours(-2));
            var status = CatalogLockMarker.Read(catalog);
            Assert.True(status.Present);
            Assert.Equal("old-host/42", status.Owner);
            Assert.NotNull(status.AcquiredUtc);
        }
        finally { Directory.Delete(folder, true); }
    }
}
