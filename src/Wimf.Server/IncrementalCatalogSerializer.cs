using System.Globalization;
using System.IO.Compression;
using System.Text;
using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Serialize an incremental path projection in legacy format, to a NEW output only.
/// The caller owns validation and promotion; this writer never overwrites a catalog.</summary>
public static class IncrementalCatalogSerializer
{
    public readonly record struct Entry(string Extension, ulong Size, long Day);
    public static int Write(string rootPath, string name, IReadOnlyDictionary<string, Entry> entries, string output)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var target = Path.GetFullPath(output);
        if (File.Exists(target)) throw new IOException("Candidate output already exists.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (target.Equals(root, comparison) || target.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Candidate output must be outside the indexed root.");
        if (name.Contains('\n') || name.Contains('\r')) throw new InvalidDataException("Invalid catalog name.");
        var cmp = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var children = new Dictionary<string, List<string>>(cmp);
        foreach (var (path, state) in entries)
        {
            if (!Path.IsPathFullyQualified(path) || !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                throw new InvalidDataException("Entry outside root.");
            var fileName = Path.GetFileName(path);
            if (fileName.IndexOfAny([';', '\n', '\r']) >= 0 || fileName.Length == 0)
                throw new InvalidDataException("Unsupported entry name.");
            var parent = Path.GetDirectoryName(path)!;
            if (parent != root && (!entries.TryGetValue(parent, out var parentState) || parentState.Extension != "folder"))
                throw new InvalidDataException("Missing parent folder.");
            if (state.Extension == "folder" && state.Size != 0)
                throw new InvalidDataException("Invalid directory size.");
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<string>();
            list.Add(path);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var gzip = target.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true) : null)
            using (var writer = new StreamWriter((Stream?)gzip ?? stream, new UTF8Encoding(false)))
            {
                writer.WriteLine("[WIMF Catalog]");
                writer.WriteLine(name);
                writer.WriteLine(root);
                writer.WriteLine(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
                writer.WriteLine();
                var stack = new Stack<(string Path, int ParentId)>();
                stack.Push((root, 0));
                var nextId = 0;
                while (stack.TryPop(out var folder))
                {
                    if (!children.TryGetValue(folder.Path, out var items)) continue;
                    items.Sort((a, b) => StringComparer.Ordinal.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                    var dirs = new List<(string Path, int ParentId)>();
                    foreach (var path in items)
                    {
                        var state = entries[path];
                        writer.WriteLine(string.Join(";", Path.GetFileName(path), state.Extension,
                            state.Size.ToString(CultureInfo.InvariantCulture),
                            folder.ParentId.ToString(CultureInfo.InvariantCulture),
                            state.Day.ToString(CultureInfo.InvariantCulture)));
                        var id = ++nextId;
                        if (state.Extension == "folder") dirs.Add((path, id));
                    }
                    for (var i = dirs.Count - 1; i >= 0; i--) stack.Push(dirs[i]);
                }
                if (nextId != entries.Count) throw new InvalidDataException("Unreachable entries.");
            }
            File.Move(temp, target, overwrite: false);
            return entries.Count;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
