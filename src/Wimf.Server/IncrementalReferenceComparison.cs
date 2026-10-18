using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>
/// Safe reference comparison for incremental-indexing development.
/// Builds a full reference catalog in a disposable temporary directory.
/// Never writes to the user's input catalog.
/// </summary>
public static class IncrementalReferenceComparison
{
    public sealed record Result(int Previous, int Reference, int Added, int Removed,
        int Modified, int Unchanged, bool FullScanRequired);

    public static Result Compare(string rootPath, string previousCatalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousCatalogPath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var previous = LegacyCatalogReader.ReadFile(previousCatalogPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(previous.Header.RootPath)),
            root, comparison))
            throw new InvalidDataException("Catalog root does not match requested root.");
        if (previous.Diagnostics.Count > 0)
            throw new InvalidDataException("Previous catalog contains invalid entries.");

        var tempDirectory = Path.Combine(Path.GetTempPath(), "wimf-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var target = Path.Combine(tempDirectory, "reference.txt.gz");
            LegacyCatalogWriter.Build(root, target, previous.Header.Name);
            var reference = LegacyCatalogReader.ReadFile(target);
            if (reference.Diagnostics.Count > 0)
                throw new InvalidDataException("Reference catalog contains invalid entries.");
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var before = previous.EnumerateEntries().ToDictionary(
                entry => previous.GetFullPath(entry.Id),
                entry => (entry.Extension, entry.Size, Day: entry.ModifiedAt.ToUnixTimeSeconds() / 86400),
                comparer);
            var added = 0;
            var modified = 0;
            var unchanged = 0;
            var count = 0;
            foreach (var entry in reference.EnumerateEntries())
            {
                count++;
                var path = reference.GetFullPath(entry.Id);
                var state = (entry.Extension, entry.Size, Day: entry.ModifiedAt.ToUnixTimeSeconds() / 86400);
                if (!before.Remove(path, out var old)) added++;
                else if (old.Equals(state)) unchanged++;
                else modified++;
            }
            var removed = before.Count;
            return new Result(previous.EnumerateEntries().Count(), count, added,
                removed, modified, unchanged, FullScanRequired: true);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
