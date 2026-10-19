using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Wimf.Catalog;
using FileCatalog = Wimf.Catalog.Catalog;

namespace Wimf.Server;

/// <summary>Explicitly opt-in actions, executed on the machine and session running the daemon.</summary>
public static class LocalFileActions
{
    public static string Resolve(FileCatalog[] snapshot, int catalogIndex, int id, string expectedPath)
    {
        if (catalogIndex < 0 || catalogIndex >= snapshot.Length || string.IsNullOrWhiteSpace(expectedPath))
            throw new ArgumentException("Unknown catalog or missing path.");

        var catalog = snapshot[catalogIndex];
        if (!catalog.TryGetEntryData(id, out var entry))
            throw new ArgumentException("Unknown file record.");

        // A path supplied by the browser is never used as an operation target.
        // Reconstruct it from the currently published catalog, rejecting stale search results.
        var reconstructed = catalog.GetFullPath(id);
        if (!string.Equals(expectedPath, reconstructed, StringComparison.Ordinal))
            throw new InvalidOperationException("Search results are stale; search again.");

        var root = catalog.Header.RootPath;
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidDataException("Catalog root is not an absolute path on this system.");

        // Only leaf names can be accessed, never traversal or absolute paths embedded in records.
        if (entry.Name is "." or ".." || entry.Name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidDataException("Catalog record has an unsafe filename.");

        var absoluteRoot = Path.GetFullPath(root);
        var absolutePath = Path.GetFullPath(reconstructed);
        var relative = Path.GetRelativePath(absoluteRoot, absolutePath);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative == ".")
            throw new InvalidDataException("File path is outside its catalog root.");

        if (entry.IsDirectory ? !Directory.Exists(absolutePath) : !File.Exists(absolutePath))
            throw new FileNotFoundException("File is absent or inaccessible.", absolutePath);
        return absolutePath;
    }

    public static void Execute(string action, string absolutePath)
    {
        if (action == "copy")
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(
                    "Native file clipboard copy currently requires an interactive Windows session.");
            WindowsFileClipboard.Copy(absolutePath);
            return;
        }

        ProcessStartInfo start;
        if (action == "open")
        {
            // Do not start executables or scripts directly from a web request.
            var ext = Path.GetExtension(absolutePath);
            if (new[] { ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf", ".msi",
                        ".scr", ".lnk", ".url", ".sh", ".desktop", ".app" }
                .Contains(ext, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Opening executables and scripts is not allowed.");
            if (OperatingSystem.IsWindows() && Directory.Exists(absolutePath))
            {
                // ShellExecute on a folder can open Explorer and still report a Win32
                // association error. Launch Explorer explicitly instead.
                start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                start.ArgumentList.Add(absolutePath);
            }
            else if (OperatingSystem.IsWindows())
                start = new ProcessStartInfo(absolutePath) { UseShellExecute = true };
            else if (OperatingSystem.IsLinux())
            {
                start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                start.ArgumentList.Add(absolutePath);
            }
            else throw new PlatformNotSupportedException("File opening is not supported on this OS.");
        }
        else if (action == "reveal")
        {
            if (OperatingSystem.IsWindows())
            {
                start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                start.ArgumentList.Add("/select," + absolutePath);
            }
            else if (OperatingSystem.IsLinux())
            {
                // xdg-open opens the containing directory; selection depends on desktop environment.
                start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                start.ArgumentList.Add(Path.GetDirectoryName(absolutePath)!);
            }
            else throw new PlatformNotSupportedException("Folder opening is not supported on this OS.");
        }
        else throw new ArgumentException("Unknown file action.", nameof(action));

        using var process = Process.Start(start);
        if (process is null) throw new IOException("Unable to start the associated application.");
    }

    /// <summary>Windows Explorer-compatible CF_HDROP (file reference, not filename text).</summary>
    private static class WindowsFileClipboard
    {
        private const uint GmemMoveable = 0x0002;
        private const uint CfHdrop = 15;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint GlobalAlloc(uint flags, nuint bytes);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint GlobalLock(nint memory);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(nint memory);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint GlobalFree(nint memory);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowEx(uint exStyle, string className, string windowName,
            uint style, int x, int y, int width, int height, nint parent, nint menu,
            nint instance, nint param);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(nint window);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(nint hwnd);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint SetClipboardData(uint format, nint memory);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        public static void Copy(string path)
        {
            // DROPFILES is 20 bytes: pFiles=20, pt=(0,0), fNC=false, fWide=true.
            // Followed by a UTF-16 double-NUL-terminated path list.
            var data = Encoding.Unicode.GetBytes(path + "\0\0");
            var memory = GlobalAlloc(GmemMoveable, (nuint)(20 + data.Length));
            if (memory == 0) throw new IOException("Unable to allocate clipboard memory.");
            try
            {
                var pointer = GlobalLock(memory);
                if (pointer == 0) throw new IOException("Unable to lock clipboard memory.");
                try
                {
                    Marshal.WriteInt32(pointer, 0, 20);
                    Marshal.WriteInt32(pointer, 4, 0);
                    Marshal.WriteInt32(pointer, 8, 0);
                    Marshal.WriteInt32(pointer, 12, 0);
                    Marshal.WriteInt32(pointer, 16, 1);
                    Marshal.Copy(data, 0, IntPtr.Add(pointer, 20), data.Length);
                }
                finally { GlobalUnlock(memory); }

                // EmptyClipboard requires a non-null owner or SetClipboardData can fail.
                var owner = CreateWindowEx(0, "STATIC", "Wimf Clipboard", 0,
                    0, 0, 0, 0, 0, 0, 0, 0);
                if (owner == 0) throw new IOException("An interactive clipboard is unavailable.");
                try
                {
                    if (!OpenClipboard(owner)) throw new IOException("Clipboard is unavailable or occupied.");
                    try
                    {
                        if (!EmptyClipboard()) throw new IOException("Cannot clear clipboard.");
                        if (SetClipboardData(CfHdrop, memory) == 0)
                            throw new IOException("Cannot copy file reference to clipboard.");
                        // The clipboard owns the movable memory after a successful SetClipboardData.
                        memory = 0;
                    }
                    finally { CloseClipboard(); }
                }
                finally { DestroyWindow(owner); }
            }
            finally
            {
                if (memory != 0) GlobalFree(memory);
            }
        }
    }
}
