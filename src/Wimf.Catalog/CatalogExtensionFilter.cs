using System.IO.Compression;
using System.Text;

namespace Wimf.Catalog;

/// <summary>Optional per-catalog extension allowlist, with legacy record IDs rebuilt after pruning.</summary>
public static class CatalogExtensionFilter
{
    public static readonly string[] DefaultExtensions =
        ["pdf", "doc", "docx", "xls", "xlsx", "jpg", "png", "ppt", "pptx"];

    public static string[] Normalize(IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in extensions)
        {
            if (string.IsNullOrWhiteSpace(entry))
                throw new ArgumentException("indexing.extensions cannot contain empty names.");
            var ext = entry.Trim().TrimStart('.');
            if (ext.Length == 0 || ext.Any(c => !char.IsAsciiLetterOrDigit(c)))
                throw new ArgumentException("indexing.extensions must contain simple file extensions.");
            normalized.Add(ext.ToLowerInvariant());
        }
        return normalized.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Transform an already-staged catalog without touching the published destination.
    /// Two streaming passes and bounded record metadata (not a full in-memory catalog tree).
    /// </summary>
    public static int Prune(string source, string destination, IEnumerable<string> extensions, bool compressed)
    {
        var allowed = new HashSet<string>(Normalize(extensions), StringComparer.OrdinalIgnoreCase);
        var parents = new List<int> { 0 };
        var keep = new List<bool> { false };
        var directory = new List<bool> { true };

        using (var reader = OpenReader(source, compressed))
        {
            for (var i = 0; i < 5; i++)
                _ = reader.ReadLine() ?? throw new InvalidDataException("Missing catalog header.");
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var fields = line.Split(';');
                if (fields.Length != 5 ||
                    !int.TryParse(fields[3], out var parent) || parent < 0 || parent >= parents.Count ||
                    !directory[parent])
                    throw new InvalidDataException("Invalid staged catalog record.");
                var isDirectory = fields[1] == "folder";
                var selected = !isDirectory && allowed.Contains(fields[1]);
                parents.Add(parent);
                directory.Add(isDirectory);
                keep.Add(selected);
                if (!selected) continue;
                for (var ancestor = parent; ancestor != 0 && !keep[ancestor]; ancestor = parents[ancestor])
                    keep[ancestor] = true;
            }
        }

        var mapping = new int[keep.Count];
        var count = 0;
        using (var reader = OpenReader(source, compressed))
        using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var gzip = compressed ? new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true) : null)
        using (var writer = new StreamWriter((Stream?)gzip ?? file, new UTF8Encoding(false)))
        {
            for (var i = 0; i < 5; i++)
                writer.WriteLine(reader.ReadLine() ?? throw new InvalidDataException("Missing catalog header."));
            string? line;
            var id = 0;
            while ((line = reader.ReadLine()) is not null)
            {
                id++;
                if (!keep[id]) continue;
                var fields = line.Split(';');
                fields[3] = mapping[parents[id]].ToString(System.Globalization.CultureInfo.InvariantCulture);
                writer.WriteLine(string.Join(";", fields));
                mapping[id] = ++count;
            }
        }
        return count;
    }

    private static StreamReader OpenReader(string path, bool compressed)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Stream stream = compressed ? new GZipStream(file, CompressionMode.Decompress) : file;
            return new StreamReader(stream, new UTF8Encoding(false, true));
        }
        catch { file.Dispose(); throw; }
    }
}
