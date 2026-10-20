namespace Wimf.Server;

/// <summary>Lightweight detection of non-cooperating catalog changes during a publication.</summary>
internal readonly record struct CatalogFileStamp(bool Exists, long Length, long LastWriteUtcTicks)
{
    public static CatalogFileStamp Read(string path)
    {
        var file = new FileInfo(path);
        return file.Exists
            ? new CatalogFileStamp(true, file.Length, file.LastWriteTimeUtc.Ticks)
            : new CatalogFileStamp(false, 0, 0);
    }
}
