using System.Security.Cryptography;

namespace Wimf.Server;

/// <summary>Lightweight detection of non-cooperating catalog changes during a publication.</summary>
internal readonly record struct CatalogFileStamp(bool Exists, long Length, long LastWriteUtcTicks)
{
    // Sample bounded sections instead of hashing multi-gigabyte catalogs on every poll.
    // This reduces timestamp+size collisions; it is not a whole-file digest.
    public static string SampleFingerprint(string path)
    {
        const int sampleSize = 64 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[sampleSize];
        var first = (int)Math.Min(stream.Length, sampleSize);
        stream.ReadExactly(buffer[..first]);
        hash.AppendData(buffer[..first]);
        if (stream.Length > sampleSize)
        {
            stream.Position = Math.Max(sampleSize, stream.Length - sampleSize);
            var last = (int)(stream.Length - stream.Position);
            stream.ReadExactly(buffer[..last]);
            hash.AppendData(buffer[..last]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static CatalogFileStamp Read(string path)
    {
        var file = new FileInfo(path);
        return file.Exists
            ? new CatalogFileStamp(true, file.Length, file.LastWriteTimeUtc.Ticks)
            : new CatalogFileStamp(false, 0, 0);
    }
}
