using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StepIK.Sandbox
{
    internal static class ProcessMemory
    {
        public static long WorkingSet(Process process)
        {
            long bytes = process.WorkingSet64;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Unity's Mono runtime can return zero for Process.WorkingSet64 on Windows.
            if (bytes <= 0)
            {
                uint size = (uint)Marshal.SizeOf<Counters>();
                var counters = new Counters { size = size };
                if (K32GetProcessMemoryInfo(new IntPtr(-1), ref counters, size))
                    bytes = (long)counters.workingSet.ToUInt64();
            }
#endif
            return bytes;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct Counters
        {
            public uint size, pageFaultCount;
            public UIntPtr peakWorkingSet, workingSet, quotaPeakPagedPool, quotaPagedPool,
                quotaPeakNonPagedPool, quotaNonPagedPool, pagefile, peakPagefile;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool K32GetProcessMemoryInfo(IntPtr process, ref Counters counters, uint size);
#endif
    }
}
