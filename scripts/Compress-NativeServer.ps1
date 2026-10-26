param(
    [Parameter(Mandatory=$true)][string] $InputFile,
    [Parameter(Mandatory=$true)][string] $OutputFile
)
$ErrorActionPreference = 'Stop'
# Windows Compression API: XPRESS_HUFF, also supported by the C++ native launcher.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
public static class WimfNativeCompression {
    [DllImport("cabinet.dll", SetLastError=true)]
    static extern bool CreateCompressor(uint algorithm, IntPtr allocation, out IntPtr handle);
    [DllImport("cabinet.dll", SetLastError=true)]
    static extern bool Compress(IntPtr compressor, byte[] source, UIntPtr sourceSize,
                                byte[] destination, UIntPtr capacity, out UIntPtr compressedSize);
    [DllImport("cabinet.dll")]
    static extern bool CloseCompressor(IntPtr compressor);
    public static void Run(string input, string output) {
        var bytes = File.ReadAllBytes(input);
        if (!CreateCompressor(4, IntPtr.Zero, out var compressor))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            // Compression may be larger than input for incompressible data.
            var capacity = checked(bytes.Length + 65536);
            var result = new byte[capacity];
            if (!Compress(compressor, bytes, (UIntPtr)(uint)bytes.Length,
                          result, (UIntPtr)(uint)result.Length, out var actual))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var size = checked((int)actual.ToUInt64());
            // Header: little-endian uncompressed length (64-bit), followed by XPRESS_HUFF bytes.
            using (var stream = File.Create(output))
            using (var writer = new BinaryWriter(stream)) {
                writer.Write((long)bytes.Length);
                writer.Write(result, 0, size);
            }
            Console.WriteLine("XPRESS_HUFF input={0} compressed={1}", bytes.Length, size + 8);
        } finally { CloseCompressor(compressor); }
    }
}
'@
[WimfNativeCompression]::Run($InputFile, $OutputFile)
