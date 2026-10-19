using System.Text;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Best-effort journal-based update. Missed notifications are corrected by periodic full scans.</summary>
public static class JournalIncrementalRefresh
{
    public sealed record Result(int Entries, int Hints, string Output);

    public static Result Run(string rootPath, string catalogPath, string journalPath,
        string outputPath, long fromOffset = 0, long? untilOffset = null,
        CancellationToken cancellationToken = default, IEnumerable<string>? exclusions = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var cmp = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var excluded = new CatalogPathExclusions(root, exclusions);
        var source = LegacyCatalogReader.ReadFile(catalogPath);
        if (source.Diagnostics.Count != 0 ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.Header.RootPath)), root, comparison))
            throw new InvalidDataException("Incremental catalog root mismatch or invalid catalog.");
        var entries = source.EnumerateEntries().ToDictionary(
            e => source.GetFullPath(e.Id),
            e => new IncrementalCatalogSerializer.Entry(e.Extension, e.Size,
                e.ModifiedAt.ToUnixTimeSeconds() / 86400), cmp);
        var hints = new HashSet<string>(cmp);
        using (var stream = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var end = untilOffset ?? stream.Length;
            if (fromOffset < 0 || end > stream.Length || end < fromOffset)
                throw new InvalidDataException("Invalid journal range.");
            stream.Position = fromOffset;
            if (end - fromOffset > 128 * 1024 * 1024)
                throw new InvalidDataException("Journal batch is too large.");
            var bytes = new byte[checked((int)(end - fromOffset))];
            stream.ReadExactly(bytes);
            if (bytes.Length > 0 && bytes[^1] != (byte)'\n')
                throw new InvalidDataException("Journal boundary is not a complete line.");
            foreach (var line in new UTF8Encoding(false, true).GetString(bytes).Split('\n',
                StringSplitOptions.RemoveEmptyEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parts = line.TrimEnd('\r').Split('\t');
                if (parts[0] is "S" or "D" or "K")
                {
                    // Validate structural records as well: silently ignoring malformed
                    // lines could acknowledge journal corruption permanently.
                    var expected = parts[0] == "K" ? 4 : parts[0] == "D" ? 2 : 3;
                    if (parts.Length != expected)
                        throw new InvalidDataException("Invalid journal metadata record.");
                    continue;
                }
                if (parts[0] != "E" || parts.Length != 3)
                    throw new InvalidDataException("Unknown or invalid journal record.");
                string path;
                try { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                    new UTF8Encoding(false, true).GetString(Convert.FromBase64String(parts[2])))); }
                catch (Exception ex) when (ex is FormatException or DecoderFallbackException or ArgumentException)
                { throw new InvalidDataException("Invalid journal path.", ex); }
                if (!path.Equals(root, comparison) &&
                    !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                    throw new InvalidDataException("Journal path outside configured root.");
                if (!path.Equals(root, comparison)) hints.Add(path);
            }
        }
        foreach (var path in hints.OrderBy(p => p.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var stale in entries.Keys.Where(p => p.Equals(path, comparison) ||
                p.StartsWith(path + Path.DirectorySeparatorChar, comparison)).ToArray())
                entries.Remove(stale);
            if (excluded.Contains(path)) continue;
            if (File.Exists(path))
            {
                TryAddFile(path);
            }
            else if (Directory.Exists(path))
            {
                var stack = new Stack<string>();
                stack.Push(path);
                while (stack.TryPop(out var directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (excluded.Contains(directory)) continue;
                    if (!directory.Equals(root, comparison))
                        entries[directory] = new("folder", 0, Day(Directory.GetLastWriteTimeUtc(directory)));
                    foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                    {
                        if (excluded.Contains(child)) continue;
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0) stack.Push(child);
                        else TryAddFile(child);
                    }
                }
            }
        }
        // The staged output is a sibling of production; never publish it before validation.
        var output = Path.GetFullPath(outputPath);
        var count = IncrementalCatalogSerializer.Write(root, source.Header.Name, entries, output);
        var check = LegacyCatalogReader.ReadFile(output);
        if (check.Diagnostics.Count != 0 || check.EnumerateEntries().Count() != count)
        {
            File.Delete(output);
            throw new InvalidDataException("Incremental round-trip validation failed.");
        }
        return new(count, hints.Count, output);

        void TryAddFile(string path)
        {
            if (excluded.Contains(path)) return;
            var name = Path.GetFileName(path);
            if (name.IndexOfAny([';', '\n', '\r']) >= 0) return;
            var info = new FileInfo(path);
            entries[path] = new(info.Extension.TrimStart('.'), checked((ulong)info.Length),
                Day(info.LastWriteTimeUtc));
        }
    }
    private static long Day(DateTime time) => new DateTimeOffset(time, TimeSpan.Zero).ToUnixTimeSeconds() / 86400;
}
