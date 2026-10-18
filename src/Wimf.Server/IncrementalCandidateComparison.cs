using System.Text;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Experimental in-memory catalog projection validated against a full scan.
/// This is not a publication mechanism: SMB notifications are never exhaustive.</summary>
public static class IncrementalCandidateComparison
{
    private readonly record struct State(string Extension, ulong Size, long Day);
    public sealed record Result(int Candidate, int Reference, int Missing, int Extra,
        int Different, int Invalid, int Hints, bool Equal, bool FullScanRequired);

    public static Result Compare(string rootPath, string catalogPath, string journalPath,
        Action<string>? onMismatch = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var previous = LegacyCatalogReader.ReadFile(catalogPath);
        if (previous.Diagnostics.Count != 0 ||
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(previous.Header.RootPath)).Equals(root, comparison))
            throw new InvalidDataException("Catalog invalid or root mismatch.");
        var candidate = previous.EnumerateEntries().ToDictionary(
            e => previous.GetFullPath(e.Id),
            e => new State(e.Extension, e.Size, e.ModifiedAt.ToUnixTimeSeconds() / 86400), comparer);
        var hints = new HashSet<string>(comparer);
        var invalid = 0;
        foreach (var line in File.ReadLines(journalPath))
        {
            var parts = line.Split('\t');
            if (parts[0] is not ("E" or "K")) continue;
            if (parts.Length != (parts[0] == "K" ? 4 : 3)) { invalid++; continue; }
            try
            {
                var raw = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(parts[^1]));
                if (!Path.IsPathFullyQualified(raw)) { invalid++; continue; }
                var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
                if (!path.Equals(root, comparison) &&
                    !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                { invalid++; continue; }
                hints.Add(path);
            }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException or ArgumentException
                                       or NotSupportedException or PathTooLongException)
            { invalid++; }
        }

        // Apply only hints. A directory rename/delete replaces its entire subtree.
        foreach (var path in hints.OrderBy(x => x.Length).ThenBy(x => x, comparer))
        {
            if (Directory.Exists(path))
            {
                foreach (var stale in candidate.Keys.Where(p => p.Equals(path, comparison) ||
                    p.StartsWith(path + Path.DirectorySeparatorChar, comparison)).ToArray())
                    candidate.Remove(stale);
                var pending = new Stack<string>();
                pending.Push(path);
                while (pending.Count != 0)
                {
                    var directory = pending.Pop();
                    if (!directory.Equals(root, comparison))
                        candidate[directory] = new State("folder", 0, Day(Directory.GetLastWriteTimeUtc(directory)));
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            throw new InvalidDataException("Reparse point encountered; full scan required.");
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                        else candidate[entry] = ReadFile(entry);
                    }
                }
            }
            else if (File.Exists(path))
                candidate[path] = ReadFile(path);
            else
            {
                foreach (var stale in candidate.Keys.Where(p => p.Equals(path, comparison) ||
                    p.StartsWith(path + Path.DirectorySeparatorChar, comparison)).ToArray())
                    candidate.Remove(stale);
            }
        }

        var candidateSize = candidate.Count;
        var temp = Path.Combine(Path.GetTempPath(), "wimf-candidate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var referencePath = Path.Combine(temp, "reference.txt.gz");
            LegacyCatalogWriter.Build(root, referencePath, previous.Header.Name);
            var reference = LegacyCatalogReader.ReadFile(referencePath);
            if (reference.Diagnostics.Count != 0) throw new InvalidDataException("Invalid reference.");
            int missing = 0, extra = 0, different = 0;
            foreach (var entry in reference.EnumerateEntries())
            {
                var path = reference.GetFullPath(entry.Id);
                var state = new State(entry.Extension, entry.Size, entry.ModifiedAt.ToUnixTimeSeconds() / 86400);
                if (!candidate.Remove(path, out var actual))
                { missing++; onMismatch?.Invoke("[CANDIDATE-MISSING] " + path); }
                else if (actual != state)
                { different++; onMismatch?.Invoke("[CANDIDATE-DIFFERENT] " + path); }
            }
            extra = candidate.Count;
            foreach (var path in candidate.Keys.Take(30))
                onMismatch?.Invoke("[CANDIDATE-EXTRA] " + path);
            var referenceCount = reference.EnumerateEntries().Count();
            return new Result(candidateSize, referenceCount, missing, extra, different,
                invalid, hints.Count, missing == 0 && extra == 0 && different == 0 && invalid == 0, true);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static long Day(DateTime utc) =>
        new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds() / 86400;

    private static State ReadFile(string path)
    {
        var info = new FileInfo(path);
        var extension = Path.GetExtension(path).TrimStart('.');
        return new State(extension, checked((ulong)info.Length),
            Day(info.LastWriteTimeUtc));
    }
}
