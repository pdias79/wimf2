using System.Text;

namespace Wimf.Server;

/// <summary>
/// Non-mutating prototype: counts entries below directories reported by the
/// SMB watcher. It must not be used to decide that a catalog is synchronized.
/// </summary>
public static class IncrementalScanPreview
{
    public sealed record Result(int Directories, long Files, long Folders, long Skipped,
        long InvalidRecords, bool FullScanRequired);

    public static Result Analyze(string rootPath, string journalPath, int lastRecords = 0,
        Action<string>? onTarget = null, Action<long, long>? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var dirty = new HashSet<string>(comparer);
        if (lastRecords < 0 || lastRecords > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(lastRecords));
        long invalid = 0;
        // Tail sampling is diagnostic only: older changes may be omitted.
        var selectedLines = lastRecords == 0
            ? File.ReadLines(journalPath)
            : File.ReadLines(journalPath).TakeLast(lastRecords);
        foreach (var line in selectedLines)
        {
            if (!line.StartsWith("D\t", StringComparison.Ordinal))
            {
                invalid++;
                continue;
            }
            try
            {
                var path = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(line[2..]));
                if (path.Length == 0 || !Path.IsPathFullyQualified(path))
                {
                    invalid++;
                    continue;
                }
                var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                if (!canonical.Equals(root, comparison) &&
                    !canonical.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                {
                    invalid++;
                    continue;
                }
                dirty.Add(canonical);
            }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException or ArgumentException
                                       or NotSupportedException or PathTooLongException)
            {
                invalid++;
            }
        }

        // A parent covers all descendants. Keep the smallest safe work set.
        var targets = new List<string>();
        foreach (var path in dirty.OrderBy(p => p.Length).ThenBy(p => p, comparer))
        {
            if (!targets.Any(parent => path.Equals(parent, comparison) ||
                                      path.StartsWith(parent + Path.DirectorySeparatorChar, comparison)))
                targets.Add(path);
        }

        long files = 0, folders = 0, skipped = 0;
        foreach (var target in targets)
        {
            onTarget?.Invoke(target);
            if (!Directory.Exists(target))
            {
                skipped++; // Could be a deletion/rename; a full reconciliation remains necessary.
                continue;
            }
            var stack = new Stack<string>();
            stack.Push(target);
            while (stack.Count > 0)
            {
                var directory = stack.Pop();
                folders++;
                if (folders % 500 == 0) onProgress?.Invoke(files, folders);
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            // Do not cross reparse points or symlink boundaries.
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                            if ((attributes & FileAttributes.Directory) != 0) stack.Push(entry);
                            else files++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            skipped++;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }
        onProgress?.Invoke(files, folders);
        return new Result(targets.Count, files, folders, skipped, invalid,
            FullScanRequired: true);
    }
}
