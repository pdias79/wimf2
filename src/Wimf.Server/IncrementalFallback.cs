using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Safely materializes a complete fallback catalog at a new destination.
/// The source catalog is never replaced; a failed scan leaves no output.</summary>
public static class IncrementalFallback
{
    public sealed record Result(string Output, int Entries, string Mode);

    public static Result BuildFull(string rootPath, string originalCatalog, string newOutput)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var source = Path.GetFullPath(originalCatalog);
        var destination = Path.GetFullPath(newOutput);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (source.Equals(destination, comparison))
            throw new InvalidDataException("Fallback output cannot overwrite source catalog.");
        if (File.Exists(destination))
            throw new IOException("Fallback output already exists.");
        if (destination.Equals(root, comparison) ||
            destination.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Fallback output must be outside indexed root.");
        var old = LegacyCatalogReader.ReadFile(source);
        if (old.Diagnostics.Count != 0 ||
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(old.Header.RootPath)).Equals(root, comparison))
            throw new InvalidDataException("Invalid previous catalog or root mismatch.");
        // The writer commits only after a complete successful traversal.
        var entries = LegacyCatalogWriter.Build(root, destination, old.Header.Name);
        var current = LegacyCatalogReader.ReadFile(destination);
        if (current.Diagnostics.Count != 0 || current.EnumerateEntries().Count() != entries)
        {
            File.Delete(destination);
            throw new InvalidDataException("Fallback output failed catalog validation.");
        }
        return new Result(destination, entries, "full");
    }
}
