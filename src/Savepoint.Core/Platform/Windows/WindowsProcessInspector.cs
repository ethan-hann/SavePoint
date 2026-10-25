using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Savepoint.Core.Platform.Windows;

/// <summary>Lists processes through the .NET process API and reads each image path with the limited-information right.</summary>
public sealed class WindowsProcessInspector : IProcessInspector
{
    private const uint _processQueryLimitedInformation = 0x1000;
    private const uint _snapProcess = 0x2;
    private static readonly IntPtr _invalidHandle = new(-1);

    /// <inheritdoc />
    public IReadOnlyList<RunningProcess> List()
    {
        var result = new List<RunningProcess>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return result;
        }

        var parents = ReadParents();
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var (path, started) = ReadImageAndStart(process.Id);
                    int? parent = parents.TryGetValue(process.Id, out var ppid) ? ppid : null;
                    result.Add(new RunningProcess(process.Id, process.ProcessName, path, null, parent, started));
                }
                catch (Exception ex) when (IsReadFailure(ex))
                {
                    // Exited between listing and reading, or off limits: skip it.
                }
            }
        }

        return result;
    }

    private static bool IsReadFailure(Exception ex) =>
        ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException;

    // One snapshot gives every process's parent id at once; a failed snapshot leaves them all unknown.
    private static Dictionary<int, int> ReadParents()
    {
        var parents = new Dictionary<int, int>();
        if (!OperatingSystem.IsWindows())
        {
            return parents;
        }

        var snapshot = CreateToolhelp32Snapshot(_snapProcess, 0);
        if (snapshot == _invalidHandle)
        {
            return parents;
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return parents;
    }

    // The limited-information right is granted for most processes, where MainModule needs far more and is slow.
    private static (string? Path, DateTimeOffset? Started) ReadImageAndStart(int pid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (null, null);
        }

        var handle = OpenProcess(_processQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return (null, null);
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            var path = QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
            DateTimeOffset? started = GetProcessTimes(handle, out var created, out _, out _, out _) && created > 0
                ? DateTimeOffset.FromFileTime(created).ToUniversalTime()
                : null;
            return (path, started);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, char[] exeName, ref uint size);
}
