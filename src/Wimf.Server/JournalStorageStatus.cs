using System.Security.Cryptography;
using System.Text;

namespace Wimf.Server;

/// <summary>
/// Read-only journal storage diagnostics. An invalid cursor is treated as
/// unacknowledged data; never delete a journal on this basis.
/// </summary>
public static class JournalStorageStatus
{
    public sealed record Snapshot(long Bytes, long AcknowledgedBytes, long PendingBytes, bool CursorValid);

    public static Snapshot Read(string catalog, string journal)
    {
        if (!File.Exists(journal)) return new(0, 0, 0, false);
        var length = new FileInfo(journal).Length;
        var target = Path.GetFullPath(catalog);
        var marker = IncrementalCursorPath.For(catalog, journal);
        if (!File.Exists(target) || !File.Exists(marker))
            return new(length, 0, length, false);
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(journal).ToUpperInvariant())));
        var parts = File.ReadAllText(marker).Split('\t');
        if (parts.Length != 3 || parts[0] != signature ||
            !long.TryParse(parts[1], out var offset) ||
            !long.TryParse(parts[2], out var ticks) ||
            offset < 0 || offset > length ||
            File.GetLastWriteTimeUtc(target).Ticks != ticks)
            return new(length, 0, length, false);
        return new(length, offset, length - offset, true);
    }
}
