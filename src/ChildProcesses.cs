using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VideoMixApp
{
    // Every ffmpeg/ffprobe is placed in one Windows job object that is closed together with Axe, so a
    // crash or "end task" never leaves encoders running and writing files in the background.
    internal static class ChildProcesses
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);

        [DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryPriorityInformation
        {
            public uint MemoryPriority;
        }

        private const int ProcessMemoryPriority = 0;
        private const uint MemoryPriorityLow = 2;

        // Windows 8 and later; on Windows 7 the entry point is missing and the call is skipped.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref MemoryPriorityInformation info, int size);

        private static bool _noMemoryPriority;

        // An encoder (or the speech recognizer) should leave the PC usable: below-normal CPU priority
        // (it still gets all idle CPU), and since v2.8.3 low memory priority, so when memory runs short
        // Windows takes pages from it before it takes them from the programs the user is working in.
        public static void Background(Process p)
        {
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            if (_noMemoryPriority) return;
            try
            {
                var info = new MemoryPriorityInformation { MemoryPriority = MemoryPriorityLow };
                SetProcessInformation(p.Handle, ProcessMemoryPriority, ref info, Marshal.SizeOf(typeof(MemoryPriorityInformation)));
            }
            catch (EntryPointNotFoundException)
            {
                _noMemoryPriority = true;
            }
            catch
            {
                // The process may already have ended; nothing to do.
            }
        }

        private static readonly object Gate = new object();
        private static IntPtr _job;
        private static bool _failed;

        public static void Add(Process p)
        {
            try
            {
                IntPtr job = Job();
                if (job != IntPtr.Zero) AssignProcessToJobObject(job, p.Handle);
            }
            catch
            {
                // Not fatal: the process still runs, it just is not tied to Axe's lifetime.
            }
        }

        private static IntPtr Job()
        {
            lock (Gate)
            {
                if (_job != IntPtr.Zero || _failed) return _job;
                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                var info = new ExtendedLimits();
                info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                if (job == IntPtr.Zero || !SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(typeof(ExtendedLimits))))
                {
                    _failed = true;
                    return IntPtr.Zero;
                }
                // The handle is intentionally never closed: Windows closes it when Axe exits.
                _job = job;
                return job;
            }
        }
    }
}
