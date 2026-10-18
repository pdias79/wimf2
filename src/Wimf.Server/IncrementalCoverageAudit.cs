using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>
/// Diagnostic oracle: checks whether filesystem changes relative to a previous
/// catalog were covered by an exact-path SMB journal. Does not build or publish
/// an incremental catalog because SMB notifications are not exhaustive.
/// </summary>
public static class IncrementalCoverageAudit
{
    public sealed record Result(int Changes, int Covered, int Uncovered, int InvalidRecords,
        bool FullScanRequired);

    public static Result Analyze(string rootPath, string previousCatalogPath, string journalPath,
        int lastRecords = 0, Action<string>? onUncovered = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (lastRecords < 0 || lastRecords > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(lastRecords));
        var prior = LegacyCatalogReader.ReadFile(previousCatalogPath);
        if (prior.Diagnostics.Count != 0 ||
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(prior.Header.RootPath)).Equals(root, comparison))
            throw new InvalidDataException("Invalid catalog or mismatching catalog root.");

        var previous = prior.EnumerateEntries().ToDictionary(
            e => prior.GetFullPath(e.Id),
            e => (e.Extension, e.Size, Day: e.ModifiedAt.ToUnixTimeSeconds() / 86400), comparer);
        var temp = Path.Combine(Path.GetTempPath(), "wimf-coverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var referencePath = Path.Combine(temp, "reference.txt.gz");
            LegacyCatalogWriter.Build(root, referencePath, prior.Header.Name);
            var currentCatalog = LegacyCatalogReader.ReadFile(referencePath);
            if (currentCatalog.Diagnostics.Count != 0) throw new InvalidDataException("Invalid reference catalog.");
            var changed = new HashSet<string>(comparer);
            foreach (var e in currentCatalog.EnumerateEntries())
            {
                var path = currentCatalog.GetFullPath(e.Id);
                var state = (e.Extension, e.Size, Day: e.ModifiedAt.ToUnixTimeSeconds() / 86400);
                if (!previous.Remove(path, out var old) || old != state)
                    changed.Add(path);
            }
            changed.UnionWith(previous.Keys);

            var hints = new HashSet<string>(comparer);
            var invalid = 0;
            var lines = lastRecords == 0 ? File.ReadLines(journalPath) : File.ReadLines(journalPath).TakeLast(lastRecords);
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts[0] is not ("E" or "K")) continue;
                if (parts.Length != (parts[0] == "K" ? 4 : 3)) { invalid++; continue; }
                try
                {
                    var raw = new System.Text.UTF8Encoding(false, true).GetString(Convert.FromBase64String(parts[^1]));
                    if (!Path.IsPathFullyQualified(raw)) { invalid++; continue; }
                    var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
                    if (!path.Equals(root, comparison) &&
                        !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                    {
                        invalid++;
                        continue;
                    }
                    hints.Add(path);
                }
                catch (Exception ex) when (ex is FormatException or System.Text.DecoderFallbackException
                                           or ArgumentException or NotSupportedException)
                {
                    invalid++;
                }
            }

            int covered = 0;
            foreach (var path in changed.OrderBy(x => x, comparer))
            {
                if (hints.Any(hint => path.Equals(hint, comparison) ||
                    path.StartsWith(hint + Path.DirectorySeparatorChar, comparison)))
                    covered++;
                else onUncovered?.Invoke(path);
            }
            return new Result(changed.Count, covered, changed.Count - covered, invalid, true);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
