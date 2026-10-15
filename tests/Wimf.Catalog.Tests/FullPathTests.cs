using System.Text;
using Wimf.Catalog;
using Xunit;

namespace Wimf.Catalog.Tests;

public class FullPathTests
{
    private static Wimf.Catalog.Catalog Parse(string root, params string[] records)
    {
        var text = string.Join('\n',
            new[] { "[WIMF Catalog]", "Paths", root, "0", "txt folder" }
                .Concat(records)) + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return LegacyCatalogReader.Read(stream, compressed: false);
    }

    [Theory]
    [InlineData(@"C:\Data\", @"C:\Data\Données\Sous-dossier\résumé.txt")]
    [InlineData(@"C:\Data", @"C:\Data\Données\Sous-dossier\résumé.txt")]
    [InlineData("/mnt/files/", "/mnt/files/Données/Sous-dossier/résumé.txt")]
    [InlineData("/", "/Données/Sous-dossier/résumé.txt")]
    public void ReconstructsDeepPathsAndKeepsPlatformSpecificSeparators(
        string root, string expected)
    {
        var catalog = Parse(root,
            "Données;folder;0;0;3",
            "Sous-dossier;folder;0;1;3",
            "résumé.txt;txt;12;2;3");

        Assert.Equal(expected, catalog.GetFullPath(3));
        Assert.Equal(expected, catalog.GetFullPath(3));
        Assert.Equal(root.TrimEnd('\\', '/') + (root.Contains('\\') ? "\\" : "/") +
                     "Données" + (root.Contains('\\') ? "\\" : "/") + "Sous-dossier" +
                     (root.Contains('\\') ? "\\" : "/") + "résumé.txt", expected);
    }

    [Fact]
    public void RootLevelFileHasOnlyOneSeparator()
    {
        var catalog = Parse(@"C:\Test\\", "a.txt;txt;1;0;3");
        Assert.Equal(@"C:\Test\a.txt", catalog.GetFullPath(1));
    }

    [Fact]
    public void CyclicParentsAreRejectedWithoutUnboundedTraversal()
    {
        var catalog = Parse("/root",
            "first;folder;0;2;3",
            "second;folder;0;1;3",
            "file.txt;txt;5;1;3");
        Assert.Throws<InvalidDataException>(() => catalog.GetFullPath(3));
    }

    [Fact]
    public void SelfReferencingFolderIsRejected()
    {
        var catalog = Parse("/root",
            "folder;folder;0;1;3",
            "file.txt;txt;5;1;3");
        Assert.Throws<InvalidDataException>(() => catalog.GetFullPath(2));
    }

    [Fact]
    public void NonFolderParentAndMissingParentAreRejected()
    {
        var nonFolder = Parse("/root",
            "first.txt;txt;1;0;3",
            "second.txt;txt;2;1;3");
        Assert.Contains("not a directory",
            Assert.Throws<InvalidDataException>(() => nonFolder.GetFullPath(2)).Message);
        var missing = Parse("/root", "file.txt;txt;5;999;3");
        Assert.Contains("missing parent",
            Assert.Throws<InvalidDataException>(() => missing.GetFullPath(1)).Message);
    }

    [Fact]
    public void MissingSlotPreservesRecordIdAndCannotResolvePath()
    {
        var catalog = Parse("/root", "invalid", "valid.txt;txt;5;0;3");
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.GetFullPath(1));
        Assert.Equal("/root/valid.txt", catalog.GetFullPath(2));
    }
}
