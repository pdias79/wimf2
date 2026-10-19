using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Wimf.Server;

/// <summary>Retrieves the daemon Windows shell icon for a file extension without opening any file.</summary>
internal static class FileTypeIcons
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiSmallIcon = 0x1;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint DibRgbColors = 0;
    private const uint DiNormal = 0x3;
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static byte[]? GetIcon(string extension)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(extension) ||
            extension.Length is < 1 or > 16 ||
            extension.Any(c => !char.IsAsciiLetterOrDigit(c)))
            return null;
        try { return Cache.GetOrAdd(extension.ToLowerInvariant(), CreateIcon); }
        catch (Exception exception) when (exception is ExternalException or ArgumentException or InvalidOperationException)
        { return null; }
    }

    private static byte[] CreateIcon(string extension)
    {
        const int width = 16;
        const int height = 16;
        var info = new ShellFileInfo();
        if (SHGetFileInfo("." + extension, FileAttributeNormal, ref info,
                (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes) == IntPtr.Zero ||
            info.Icon == IntPtr.Zero)
            throw new InvalidOperationException("The shell did not return an icon.");

        IntPtr screen = IntPtr.Zero, dc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            var header = new BitmapInfo
            {
                HeaderSize = 40, Width = width, Height = height, Planes = 1, BitCount = 32
            };
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new InvalidOperationException("No screen device context.");
            dc = CreateCompatibleDC(screen);
            if (dc == IntPtr.Zero) throw new InvalidOperationException("No bitmap device context.");
            bitmap = CreateDIBSection(dc, ref header, DibRgbColors, out var pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero)
                throw new InvalidOperationException("Cannot allocate an icon bitmap.");
            previous = SelectObject(dc, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                throw new InvalidOperationException("Cannot select an icon bitmap.");
            if (!DrawIconEx(dc, 0, 0, info.Icon, width, height, 0, IntPtr.Zero, DiNormal))
                throw new InvalidOperationException("Cannot draw the file type icon.");
            var rgba = new byte[width * height * 4];
            Marshal.Copy(pixels, rgba, 0, rgba.Length);
            // Some legacy shell icons contain RGB values but no alpha channel.
            if (!Enumerable.Range(0, width * height).Any(i => rgba[4 * i + 3] != 0))
                for (var i = 0; i < width * height; i++) rgba[4 * i + 3] = 255;

            var maskRowBytes = ((width + 31) / 32) * 4;
            var mask = new byte[maskRowBytes * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (rgba[(y * width + x) * 4 + 3] == 0)
                        mask[y * maskRowBytes + x / 8] |= (byte)(0x80 >> (x % 8));

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write((byte)width);
                writer.Write((byte)height);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)(40 + rgba.Length + mask.Length));
                writer.Write((uint)22);
                writer.Write((uint)40);
                writer.Write(width);
                writer.Write(height * 2);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(0);
                writer.Write((uint)rgba.Length);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(rgba);
                writer.Write(mask);
            }
            return stream.ToArray();
        }
        finally
        {
            if (previous != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            DestroyIcon(info.Icon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint HeaderSize;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo bitmapInfo, uint usage,
        out IntPtr bits, IntPtr section, uint offset);
    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height,
        uint step, IntPtr brush, uint flags);
}
