using System.Security.Cryptography;
using System.Text;

namespace Wimf.Server;

/// <summary>
/// Cursor ownership follows the host-local watcher journal, never the shared SMB catalog.
/// A legacy shared cursor is intentionally not migrated: replaying safely is preferable
/// to accepting another host's acknowledgement offset.
/// </summary>
public static class IncrementalCursorPath
{
    public static string For(string catalog, string journal)
    {
        var catalogPath = Path.GetFullPath(catalog);
        var journalPath = Path.GetFullPath(journal);
        var identity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(catalogPath.ToUpperInvariant())));
        return journalPath + "." + identity + ".incremental-cursor";
    }
}
