using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public sealed class LegacyCatalogWriterTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".txt.gz")]
    public void BuildsReadableCatalogWithNestedFoldersAndAccuratePaths(string extension)
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-build-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, "source");
        Directory.CreateDirectory(Path.Combine(root, "Documents", "Notes"));
        File.WriteAllText(Path.Combine(root, "Documents", "report.txt"), "Bonjour");
        File.WriteAllText(Path.Combine(root, "Documents", "Notes", "hello.md"), "Hello");
        var output = Path.Combine(work, "catalog" + extension);
        try
        {
            var count = LegacyCatalogWriter.Build(root, output, "Essai");
            Assert.Equal(4, count);
            var catalog = LegacyCatalogReader.ReadFile(output);
            Assert.Equal("Essai", catalog.Header.Name);
            Assert.Equal(root, catalog.Header.RootPath);
            Assert.Empty(catalog.Diagnostics);
            var paths = catalog.EnumerateEntries().ToDictionary(e => e.Name, e => catalog.GetFullPath(e.Id));
            Assert.Equal(Path.Combine(root, "Documents", "report.txt"), paths["report.txt"]);
            Assert.Equal(Path.Combine(root, "Documents", "Notes", "hello.md"), paths["hello.md"]);
            Assert.True(catalog.GetEntry(1)!.IsDirectory);
            Assert.Equal(7UL, catalog.EnumerateEntries().Single(e => e.Name == "report.txt").Size);
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void AllowsOutputInsideSourceWithoutIndexingOutputOrTemporaryFile()
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var output = Path.Combine(work, "catalog.txt");
        File.WriteAllText(output, "previous catalog");
        File.WriteAllText(Path.Combine(work, "document.txt"), "document");
        try
        {
            Assert.Equal(1, LegacyCatalogWriter.Build(work, output));
            var catalog = LegacyCatalogReader.ReadFile(output);
            Assert.Single(catalog.EnumerateEntries());
            Assert.Equal("document.txt", catalog.GetEntry(1)!.Name);
            Assert.DoesNotContain(catalog.EnumerateEntries(), e => e.Name == "catalog.txt");
            Assert.DoesNotContain(Directory.EnumerateFiles(work), p => Path.GetFileName(p).EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally { Directory.Delete(work, true); }
    }

    [Fact]
    public void FailsSafelyForUnsupportedSemicolonNames()
    {
        var work = Path.Combine(Path.GetTempPath(), "wimf-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "unsafe;file.txt"), "content");
        var output = work + ".txt.gz";
        try
        {
            Assert.Throws<InvalidDataException>(() => LegacyCatalogWriter.Build(work, output));
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(work, true);
            if (File.Exists(output)) File.Delete(output);
        }
    }
    [Fact]
    public void SkipsUnreadableSubdirectoryAndContinuesWithReadableSiblings()
    {
        if (!OperatingSystem.IsLinux()) return;
        var work = Path.Combine(Path.GetTempPath(), "wimf-build-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(work, "source");
        var blocked = Path.Combine(root, "Blocked");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "secret.txt"), "hidden");
        File.WriteAllText(Path.Combine(root, "readable.txt"), "visible");
        var output = Path.Combine(work, "catalog.txt.gz");
        try
        {
            File.SetUnixFileMode(blocked, UnixFileMode.None);
            // Root/privileged CI users may retain access regardless of Unix mode.
            var trulyBlocked = false;
            try { _ = Directory.EnumerateFileSystemEntries(blocked).ToArray(); }
            catch (UnauthorizedAccessException) { trulyBlocked = true; }
            if (!trulyBlocked) return;

            var warnings = new List<string>();
            var count = LegacyCatalogWriter.Build(root, output, onSkipped: warnings.Add);
            var catalog = LegacyCatalogReader.ReadFile(output);
            Assert.Equal(count, catalog.RecordCount);
            Assert.Contains(warnings, message => message.Contains(blocked, StringComparison.Ordinal));
            Assert.Contains(catalog.EnumerateEntries(), entry => entry.Name == "readable.txt");
            Assert.DoesNotContain(catalog.EnumerateEntries(), entry => entry.Name == "secret.txt");
            Assert.Empty(catalog.Diagnostics);
        }
        finally
        {
            if (Directory.Exists(blocked)) File.SetUnixFileMode(blocked,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(work, true);
        }
    }

}
