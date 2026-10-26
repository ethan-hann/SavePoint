using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Savepoint.Core.Git;

// One Windows job object for the life of the app that every git process (and what git starts) is put in. The job ends its members
// when its last handle closes, and the only handle is this process's, so the system ends git when SavePoint ends however that happens:
// Task Manager, a crash, a log-off. Nothing else is ever put in it: the game is started elsewhere and must outlive SavePoint.
internal static class GitProcessJob
{
    private const int _extendedLimitInformation = 9;
    private const uint _killOnJobClose = 0x2000;

    // Created on first use and never closed; closing it is what would end the processes in it.
    private static readonly Lazy<(IntPtr Handle, string? Problem)> _job = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Puts a started process in the job. A process that is already in another job (SavePoint itself may have been started in one) is
    /// nested in this one, which Windows 8 and later allow; a failure never stops the launch.
    /// </summary>
    /// <param name="process">The process just started.</param>
    /// <returns>Null when the process is in the job (or the system has no jobs); otherwise what went wrong, for the log.</returns>
    public static string? TryAssign(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var (job, problem) = _job.Value;
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            return AssignProcessToJobObject(job, process.Handle) ? null : $"the process could not be put in the job: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // Ended before it could be put in.
            return $"the process could not be put in the job: {e.Message}";
        }
    }

    private static (IntPtr Handle, string? Problem) Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (IntPtr.Zero, null);
        }

        // No security attributes: the handle is not inherited, so a process git starts cannot keep the job alive after SavePoint ends.
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            return (IntPtr.Zero, $"the job could not be created: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        var information = new ExtendedLimitInformation { Basic = new BasicLimitInformation { LimitFlags = _killOnJobClose } };
        if (!SetInformationJobObject(job, _extendedLimitInformation, ref information, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            var problem = $"the job could not be set to end its processes: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            CloseHandle(job);
            return (IntPtr.Zero, problem);
        }

        return (job, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
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
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateJobObjectW")]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimitInformation information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
