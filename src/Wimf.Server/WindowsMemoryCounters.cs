using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Wimf.Server;

/// <summary>Optional Windows-only private working set; null on other OS or unavailable API.</summary>
internal static class WindowsMemoryCounters
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public nuint SharedCommitUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(
        IntPtr process, ref ProcessMemoryCountersEx2 counters, uint size);

    public static long? TryPrivateWorkingSetBytes(Process process)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var counters = new ProcessMemoryCountersEx2();
            counters.Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>();
            return GetProcessMemoryInfo(process.Handle, ref counters, counters.Size)
                ? checked((long)counters.PrivateWorkingSetSize)
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                   or OverflowException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
