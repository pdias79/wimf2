namespace Wimf.Server;

/// <summary>Informational marker only. Reading it does not establish whether an SMB OS lock is held.</summary>
public sealed record CatalogLockMarkerStatus(bool Present, string? Owner, DateTimeOffset? AcquiredUtc,
    DateTimeOffset? LastWriteUtc, string? ReadError);

public static class CatalogLockMarker
{
    public static CatalogLockMarkerStatus Read(string catalogPath)
    {
        var path = Path.GetFullPath(catalogPath) + ".wimf.lock";
        try
        {
            if (!File.Exists(path)) return new(false, null, null, null, null);
            var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Bound malformed/stale marker reads; never take or alter the OS lock.
            using var reader = new StreamReader(stream);
            var buffer = new char[1024];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, count);
            string? owner = null;
            DateTimeOffset? acquired = null;
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("owner=", StringComparison.Ordinal))
                    owner = line[6..].TrimEnd('\r');
                else if (line.StartsWith("acquiredUtc=", StringComparison.Ordinal) &&
                         DateTimeOffset.TryParse(line[12..].TrimEnd('\r'),
                             System.Globalization.CultureInfo.InvariantCulture,
                             System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    acquired = parsed;
            }
            return new(true, owner, acquired, modified, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(true, null, null, null, "Marqueur inaccessible");
        }
    }
}
