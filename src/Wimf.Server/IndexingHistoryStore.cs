using System.Text.Json;

namespace Wimf.Server;

/// <summary>Best-effort, bounded persistent history, independent of indexing state.</summary>
public sealed class IndexingHistoryStore
{
    private const int Limit = 50;
    private readonly string _path;
    public IndexingHistoryStore(string path) => _path = Path.GetFullPath(path);

    public IndexingRunHistoryEntry[] Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var records = JsonSerializer.Deserialize<IndexingRunHistoryEntry[]>(File.ReadAllText(_path));
            if (records is null) return [];
            return records.Where(record => record is not null &&
                !string.IsNullOrWhiteSpace(record.Name) &&
                record.FinishedAt >= record.StartedAt)
                .Take(Limit).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                                    or NotSupportedException)
        {
            Console.Error.WriteLine($"[INDEXING-HISTORY] Unable to restore history: {ex.Message}");
            return [];
        }
    }

    public void Save(IReadOnlyList<IndexingRunHistoryEntry> entries)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, entries.Take(Limit).ToArray());
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
