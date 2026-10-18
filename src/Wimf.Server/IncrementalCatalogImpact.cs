using System.Text;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>
/// Read-only impact analysis against a previous catalog. This is diagnostic:
/// it does not establish a complete event history or apply catalog mutations.
/// </summary>
public static class IncrementalCatalogImpact
{
    public sealed record Result(int Paths, int CatalogEntries, int ExactMatches,
        int DescendantMatches, int Unmatched, int InvalidRecords);

    public static Result Analyze(string rootPath, string journalPath, string catalogPath,
        int lastRecords = 0, Action<string>? onImpact = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        if (lastRecords < 0 || lastRecords > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(lastRecords));

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var catalog = LegacyCatalogReader.ReadFile(catalogPath);
        var catalogRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(catalog.Header.RootPath));
        if (!string.Equals(root, catalogRoot, comparison))
            throw new InvalidDataException("Catalog root differs from the selected SMB root.");
        if (catalog.Diagnostics.Count != 0)
            throw new InvalidDataException("Cannot analyze a catalog containing invalid entries.");

        var candidates = new HashSet<string>(comparer);
        var invalid = 0;
        var lines = lastRecords == 0 ? File.ReadLines(journalPath) :
            File.ReadLines(journalPath).TakeLast(lastRecords);
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length < 3 || parts[0] is not ("E" or "K") ||
                parts[1] is not ("deleted" or "renamed-from")) continue;
            if (parts.Length != (parts[0] == "K" ? 4 : 3)) { invalid++; continue; }
            try
            {
                var raw = new UTF8Encoding(false, true).GetString(
                    Convert.FromBase64String(parts[^1]));
                if (!Path.IsPathFullyQualified(raw)) { invalid++; continue; }
                var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
                if (!path.Equals(root, comparison) &&
                    !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                {
                    invalid++;
                    continue;
                }
                candidates.Add(path);
            }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException or ArgumentException
                                       or NotSupportedException or PathTooLongException)
            {
                invalid++;
            }
        }

        var entries = 0;
        var exact = 0;
        var descendants = 0;
        var matched = new HashSet<string>(comparer);
        foreach (var entry in catalog.EnumerateEntries())
        {
            var path = catalog.GetFullPath(entry.Id);
            foreach (var changed in candidates)
            {
                if (path.Equals(changed, comparison))
                {
                    exact++;
                    entries++;
                    matched.Add(changed);
                    onImpact?.Invoke($"[CATALOG-EXACT] {path}");
                    break;
                }
                if (path.StartsWith(changed + Path.DirectorySeparatorChar, comparison))
                {
                    descendants++;
                    entries++;
                    matched.Add(changed);
                    onImpact?.Invoke($"[CATALOG-DESCENDANT] {path}");
                    break;
                }
            }
        }
        foreach (var missing in candidates.Where(path => !matched.Contains(path)).OrderBy(path => path, comparer))
            onImpact?.Invoke($"[CATALOG-MISSING] {missing}");
        return new Result(candidates.Count, entries, exact, descendants,
            candidates.Count - matched.Count, invalid);
    }
}
