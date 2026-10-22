using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

/// <summary>
/// Two independent watcher journals, one common published catalog.
/// These tests exercise sequential publication under the scheduler's cooperative lock.
/// </summary>
public sealed class MultiJournalPublicationTests
{
    [Fact]
    public void AlternatingIndependentJournalsPreservePreviouslyPublishedFiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-two-journals-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "shared-catalog.txt.gz");
            var journalA = Path.Combine(temp, "machine-a", "events");
            var journalB = Path.Combine(temp, "machine-b", "events");
            Directory.CreateDirectory(Path.GetDirectoryName(journalA)!);
            Directory.CreateDirectory(Path.GetDirectoryName(journalB)!);
            LegacyCatalogWriter.Build(root, catalog);

            var a = Path.Combine(root, "a.txt");
            File.WriteAllText(a, "A");
            File.WriteAllText(journalA, Created(a));
            PublishWithLock(root, catalog, journalA);
            AssertPaths(catalog, a);

            var b = Path.Combine(root, "b.txt");
            File.WriteAllText(b, "B");
            File.WriteAllText(journalB, Created(b));
            PublishWithLock(root, catalog, journalB);
            AssertPaths(catalog, a, b);

            // Machine A's old cursor has been replaced by B's signature.
            // A must replay rather than accidentally reuse B's byte offset.
            Assert.Equal(1, IncrementalJournalPublisher.Pending(catalog, journalA).Events);
            PublishWithLock(root, catalog, journalA);
            AssertPaths(catalog, a, b);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void IndependentJournalAfterAnotherHostPublishesDoesNotRestoreDeletedFile()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-two-journals-delete-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "source");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Path.Combine(temp, "shared-catalog.txt.gz");
            var journalA = Path.Combine(temp, "host-a.events");
            var journalB = Path.Combine(temp, "host-b.events");
            var a = Path.Combine(root, "removed.txt");
            var b = Path.Combine(root, "retained.txt");
            File.WriteAllText(a, "A");
            File.WriteAllText(b, "B");
            LegacyCatalogWriter.Build(root, catalog);

            File.Delete(a);
            File.WriteAllText(journalA, Deleted(a));
            PublishWithLock(root, catalog, journalA);
            AssertPaths(catalog, b);

            File.WriteAllText(journalB, Changed(b));
            PublishWithLock(root, catalog, journalB);
            AssertPaths(catalog, b);

            // Replaying A's now-old journal must remain idempotent.
            PublishWithLock(root, catalog, journalA);
            AssertPaths(catalog, b);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static void PublishWithLock(string root, string catalog, string journal)
    {
        using var holder = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30));
        Assert.NotNull(holder);
        IncrementalJournalPublisher.Publish(root, catalog, journal);
    }

    private static void AssertPaths(string catalog, params string[] expected)
    {
        var parsed = LegacyCatalogReader.ReadFile(catalog);
        Assert.Empty(parsed.Diagnostics);
        var actual = parsed.EnumerateEntries().Select(entry => parsed.GetFullPath(entry.Id)).ToArray();
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal),
            actual.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string Created(string path) => Event("created", path);
    private static string Deleted(string path) => Event("deleted", path);
    private static string Changed(string path) => Event("changed", path);
    private static string Event(string kind, string path) =>
        "E\t" + kind + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(path)) + "\n";
}
