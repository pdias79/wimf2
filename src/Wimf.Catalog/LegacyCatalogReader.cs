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
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);

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

        var entries = new List<CatalogEntry?> { null }; // ID 0 is the root
        var diagnostics = new List<CatalogDiagnostic>();
        string? line;
        int lineNumber = 5;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var id = entries.Count;
            // Preserve the ordinal even if parsing fails: parent references depend on it.
            var parts = line.Split(';');
            if (parts.Length != 5 || string.IsNullOrWhiteSpace(parts[0]) ||
                !ulong.TryParse(parts.ElementAtOrDefault(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ||
                !int.TryParse(parts.ElementAtOrDefault(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parentId) ||
                !long.TryParse(parts.ElementAtOrDefault(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ||
                parentId < 0)
            {
                diagnostics.Add(new CatalogDiagnostic(lineNumber, "Invalid entry format or numeric field."));
                entries.Add(null);
                continue;
            }
            try
            {
                var modified = DateTimeOffset.FromUnixTimeSeconds(checked(days * 86400));
                entries.Add(new CatalogEntry(id, parts[0], parts[1], size, parentId, modified));
            }
            catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
            {
                diagnostics.Add(new CatalogDiagnostic(lineNumber, "Entry timestamp out of range."));
                entries.Add(null);
            }
        }

        var slots = entries.ToArray();
        foreach (var entry in slots)
        {
            if (entry is null || entry.ParentId == 0) continue;
            if (entry.ParentId >= slots.Length || slots[entry.ParentId] is not { IsDirectory: true })
                diagnostics.Add(new CatalogDiagnostic(entry.Id + 5, $"Invalid parent reference {entry.ParentId}."));
        }

        return new Catalog(new CatalogHeader(name, rootPath, created, extensions), slots, diagnostics);
    }
}
