using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Wimf.Catalog;

/// <summary>Reads legacy WIMF catalog text; supports gzip and plain UTF-8.</summary>
public static class LegacyCatalogReader
{
    public static Catalog ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, compressed: path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
    }

    public static Catalog Read(Stream stream, bool compressed)
    {
        using var source = compressed
            ? new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true)
            : null;
        using var reader = new StreamReader(
            source ?? stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

        string RequireLine(string description) =>
            reader.ReadLine() ?? throw new InvalidDataException($"Missing catalog {description}.");

        var signature = RequireLine("signature").TrimStart('\uFEFF');
        if (!string.Equals(signature, "[WIMF Catalog]", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid WIMF catalog signature.");

        var name = RequireLine("name");
        var rootPath = RequireLine("root path");
        var timestamp = RequireLine("creation time");
        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            throw new InvalidDataException("Invalid catalog creation timestamp.");

        DateTimeOffset created;
        try { created = DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("Catalog timestamp out of range.", ex); }

        var extensions = RequireLine("extensions")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var entries = new List<CatalogEntryData> { default }; // ID 0 is the root
        var extensionPool = new Dictionary<string, string>(StringComparer.Ordinal);
        var diagnostics = new List<CatalogDiagnostic>();
        string? line;
        int lineNumber = 5;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            // Preserve the ordinal even for malformed lines: parent IDs reference line positions.
            // Slice the line instead of Split(';'): only retained name and new extensions allocate.
            var remaining = line.AsSpan();
            var first = remaining.IndexOf(';');
            var second = first >= 0 ? remaining[(first + 1)..].IndexOf(';') : -1;
            if (second >= 0) second += first + 1;
            var third = second >= 0 ? remaining[(second + 1)..].IndexOf(';') : -1;
            if (third >= 0) third += second + 1;
            var fourth = third >= 0 ? remaining[(third + 1)..].IndexOf(';') : -1;
            if (fourth >= 0) fourth += third + 1;

            if (first < 0 || second < 0 || third < 0 || fourth < 0 ||
                remaining[(fourth + 1)..].Contains(';') ||
                remaining[..first].IsWhiteSpace() ||
                !ulong.TryParse(remaining[(second + 1)..third], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ||
                !int.TryParse(remaining[(third + 1)..fourth], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parentId) ||
                !long.TryParse(remaining[(fourth + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ||
                parentId < 0)
            {
                diagnostics.Add(new CatalogDiagnostic(lineNumber, "Invalid entry format or numeric field."));
                entries.Add(default);
                continue;
            }
            try
            {
                _ = DateTimeOffset.FromUnixTimeSeconds(checked(days * 86400));
                var extensionSpan = remaining[(first + 1)..second];
                // Alternate dictionary lookup lets repeated extensions avoid allocating a key.
                var lookup = extensionPool.GetAlternateLookup<ReadOnlySpan<char>>();
                if (!lookup.TryGetValue(extensionSpan, out var extension))
                {
                    extension = extensionSpan.ToString();
                    extensionPool.Add(extension, extension);
                }
                entries.Add(new CatalogEntryData(remaining[..first].ToString(), extension, size, parentId, days));
            }
            catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
            {
                diagnostics.Add(new CatalogDiagnostic(lineNumber, "Entry timestamp out of range."));
                entries.Add(default);
            }
        }

        var slots = entries.ToArray();
        for (var id = 1; id < slots.Length; id++)
        {
            var entry = slots[id];
            if (entry.Name is null || entry.ParentId == 0) continue;
            if (entry.ParentId >= slots.Length || !slots[entry.ParentId].IsDirectory)
                diagnostics.Add(new CatalogDiagnostic(id + 5, $"Invalid parent reference {entry.ParentId}."));
        }

        return new Catalog(new CatalogHeader(name, rootPath, created, extensions), slots, diagnostics);
    }
}
